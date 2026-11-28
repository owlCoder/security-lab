using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Entities;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration.Audit;

[TestFixture]
[Category("AUD-01")]
[NonParallelizable]
public sealed class IdentityAuditIntegrationTests : IntegrationTestBase
{
    private const string Password = "Strong-password!2026";

    private string auditRuntimeConnection = null!;

    [OneTimeSetUp]
    public async Task PrepareAuditRuntime() =>
        auditRuntimeConnection = await AuditTestData.CreateRestrictedRuntimeAsync();

    [Test]
    public async Task LoginAndLogout_ValidAccount_ShouldPersistAuditEventsAndRevokeSession()
    {
        // Arrange
        var email = $"audit-flow-{Guid.NewGuid():N}@example.com";
        var userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        await using var factory = CreateIdentityHost(loginPermitLimit: 1000);
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        // Act
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = Password });
        using var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);

        // Assert
        var storedSession = (await SessionTestData.GetForUserAsync(Factory, userId)).Single();
        var events = (await AuditTestData.GetByActorAsync(Factory, userId)).Select(row => row.EventType).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(storedSession.RevokedAt, Is.Not.Null);
            Assert.That(events, Does.Contain(AuditEventTypes.AuthLogin));
            Assert.That(events, Does.Contain(AuditEventTypes.AuthLogout));
        });
    }

    [Test]
    public async Task LoginAndRevokeHttpFlow_ValidSession_PersistsSessionRevokedWithRequestCorrelation()
    {
        // Arrange
        var email = $"audit-revoke-{Guid.NewGuid():N}@example.com";
        var userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        await using var factory = CreateIdentityHost(loginPermitLimit: 1000);
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = Password });
        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = (await SessionTestData.GetForUserAsync(Factory, userId))
            .Single(value => value.RevokedAt == null);

        // Act
        using var revokeResponse = await client.DeleteAsync(
            $"/api/auth/sessions/{session.SessionHandle}");
        var correlationId = revokeResponse.Headers.GetValues("X-Correlation-ID").Single();

        // Assert
        var records = await AuditTestData.GetByCorrelationAsync(Factory, correlationId);
        Assert.That(records, Has.Count.EqualTo(1));
        var record = records.Single(value =>
            value.ActorId == userId
            && value.EventType == AuditEventTypes.AuthSessionRevoked
            && value.CorrelationId == correlationId);
        Assert.Multiple(() =>
        {
            Assert.That(revokeResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(record.ActorId, Is.EqualTo(userId));
            Assert.That(record.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(record.EventType, Is.EqualTo(AuditEventTypes.AuthSessionRevoked));
            Assert.That(record.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(record.ResourceType, Is.EqualTo(AuditResourceTypes.Account));
            Assert.That(record.ResourceId, Is.EqualTo(userId.ToString()));
            Assert.That(record.DetailsJson, Does.Contain("user_request"));
            Assert.That(record.DetailsJson, Does.Contain(session.SessionHandle.ToString("D")));
        });
        Assert.That((await SessionTestData.GetAsync(Factory, session.Id))!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task Login_WhenAuditWriteFails_PersistsOnlyARevokedSession()
    {
        // Arrange
        var email = $"audit-down-{Guid.NewGuid():N}@example.com";
        var userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        var writer = new Mock<ITrustedAuditWriter>();
        writer.Setup(value => value.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuditWriteResult.Unavailable());
        await using var factory = CreateIdentityHost(writer: writer.Object);
        using var scope = factory.Services.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<ILoginService>();

        // Act
        var result = await login.ExecuteAsync(email, Password, Guid.NewGuid().ToString("N"));

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(result.Session, Is.Null);

        var storedSession = (await SessionTestData.GetForUserAsync(Factory, userId)).Single();
        Assert.That(storedSession.RevokedAt, Is.Not.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Login_ThrottledAccount_ShouldPersistOneAnonymousDeniedEventWithoutCredentials(bool knownAccount)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = $"throttled-{userId:N}@example.com";
        if (knownAccount) userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        await using var factory = CreateIdentityHost();
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(first.StatusCode, Is.EqualTo(knownAccount ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));

        // Even a previously issued cookie and forged headers cannot assign an actor to throttling.
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "client-correlation");
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Act
        using var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var correlation = blocked.Headers.GetValues("X-Correlation-ID").Single();
        var body = await blocked.Content.ReadAsStringAsync();

        // Assert
        var records = await AuditTestData.GetByCorrelationAsync(Factory, correlation);
        Assert.That(records, Has.Count.EqualTo(1));
        var record = records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(blocked.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(int.TryParse(blocked.Headers.RetryAfter?.ToString(), out var seconds), Is.True);
            Assert.That(seconds, Is.InRange(1, 60));
            Assert.That(blocked.Headers.Contains("Set-Cookie"), Is.False);
            Assert.That(body, Does.Contain("rate_limited").And.Not.Contain(email).And.Not.Contain(Password));
            Assert.That(correlation, Is.Not.EqualTo("client-correlation"));
            Assert.That(record.EventType, Is.EqualTo(AuditEventTypes.AuthLoginThrottled));
            Assert.That(record.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(record.ActorKind, Is.EqualTo(AuditActorKind.Anonymous));
            Assert.That(record.ActorId, Is.Null);
            Assert.That(record.CourseId, Is.Null);
            Assert.That(record.ResourceId, Is.Null);
            Assert.That(record.ResourceType, Is.Null);
            Assert.That(record.DetailsJson, Is.EqualTo("{}"));
            Assert.That(record.OccurredAt, Is.InRange(before, DateTimeOffset.UtcNow));
        });
        Assert.That((await SessionTestData.GetForUserAsync(Factory, userId)).Count,
            Is.EqualTo(knownAccount ? 1 : 0), "The rejected request must never issue another session.");
    }

    [Test]
    public async Task ThrottledLogin_WhenDatabaseRejectsInsert_StillReturns429WithoutSession()
    {
        // Arrange
        var email = $"no-audit-{Guid.NewGuid():N}@example.com";
        await using var factory = CreateIdentityHost();
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        await AuditTestData.SetInsertAllowedAsync(false);
        try
        {
            // Act
            using var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            var correlation = blocked.Headers.GetValues("X-Correlation-ID").Single();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That(blocked.Headers.RetryAfter, Is.Not.Null);
                Assert.That(blocked.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(blocked.Headers.Contains("Set-Cookie"), Is.False);
            });
            Assert.That(await AuditTestData.GetByCorrelationAsync(Factory, correlation), Is.Empty);
            Assert.That(await SessionTestData.CountAsync(Factory), Is.Zero);
            Assert.That(await blocked.Content.ReadAsStringAsync(), Does.Contain("rate_limited"));
        }
        finally
        {
            await AuditTestData.SetInsertAllowedAsync(true);
        }
    }

    [Test]
    public async Task Login_LockoutThresholdAndLaterAttempt_ShouldPersistAnonymousFailureEvents()
    {
        // Arrange
        var email = $"lockout-audit-{Guid.NewGuid():N}@example.com";
        var userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        await using var factory = CreateIdentityHost(loginPermitLimit: 1000, maxFailedAttempts: 3);
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        const string wrongPassword = "Incorrect-audit-password!2026";
        for (var i = 0; i < 2; i++)
        {
            using var failure = await client.PostAsJsonAsync("/api/auth/login", new { email, password = wrongPassword });
            Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        var account = (await GetAsync<UserAccount>(userId))!;
        Assert.That(account.LockoutEnd, Is.Null);
        Assert.That(account.FailedLoginCount, Is.EqualTo(2));

        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "untrusted-lockout-correlation");

        // Act
        using var triggering = await client.PostAsJsonAsync("/api/auth/login", new { email, password = wrongPassword });
        var triggeringCorrelation = triggering.Headers.GetValues("X-Correlation-ID").Single();
        var body = await triggering.Content.ReadAsStringAsync();
        using var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var blockedCorrelation = blocked.Headers.GetValues("X-Correlation-ID").Single();

        // Assert
        await AssertLockedLoginAuditAsync(triggeringCorrelation, userId, email, wrongPassword);
        await AssertLockedLoginAuditAsync(blockedCorrelation, userId, email, Password);
        Assert.Multiple(() =>
        {
            Assert.That(triggering.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(triggeringCorrelation, Is.Not.EqualTo("untrusted-lockout-correlation"));
            Assert.That(blockedCorrelation, Is.Not.EqualTo(triggeringCorrelation));
            Assert.That(triggering.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(triggering.Headers.Contains("Retry-After"), Is.False);
            Assert.That(triggering.Headers.Contains("Set-Cookie"), Is.False);
            Assert.That(blocked.Headers.Contains("Set-Cookie"), Is.False);
            Assert.That(body, Does.Contain("invalid_credentials"));
        });
        Assert.That(await blocked.Content.ReadAsStringAsync(), Is.EqualTo(body));
        Assert.That((await GetAsync<UserAccount>(userId))!.LockoutEnd,
            Is.GreaterThan(DateTimeOffset.UtcNow));
        Assert.That(await SessionTestData.GetForUserAsync(Factory, userId), Is.Empty);
    }

    [Test]
    public async Task AccountLockout_WhenAuditInsertFails_StaysLockedAndIssuesNoSession()
    {
        // Arrange
        var email = $"lockout-audit-down-{Guid.NewGuid():N}@example.com";
        var userId = (await UserTestData.AddAsync(Factory, email, Password)).Id;
        await using var factory = CreateIdentityHost(loginPermitLimit: 1000, maxFailedAttempts: 1);
        using var client = factory.CreateCsrfClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await AuditTestData.SetInsertAllowedAsync(false);
        try
        {
            // Act
            using var failed = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });
            var correlation = failed.Headers.GetValues("X-Correlation-ID").Single();

            // Assert
            Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(failed.Headers.Contains("Set-Cookie"), Is.False);
            Assert.That((await GetAsync<UserAccount>(userId))!.LockoutEnd,
                Is.GreaterThan(DateTimeOffset.UtcNow));
            Assert.That(await SessionTestData.GetForUserAsync(Factory, userId), Is.Empty);
            Assert.That(await AuditTestData.GetByCorrelationAsync(Factory, correlation), Is.Empty,
                "The current contract does not guarantee a durable event when Audit storage rejects the write.");
        }
        finally
        {
            await AuditTestData.SetInsertAllowedAsync(true);
        }
        using var stillLocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(stillLocked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await AssertLockedLoginAuditAsync(stillLocked.Headers.GetValues("X-Correlation-ID").Single(), userId, email, Password);
    }

    private async Task AssertLockedLoginAuditAsync(string correlation, Guid accountId, string email, string password)
    {
        var records = await AuditTestData.GetByCorrelationAsync(Factory, correlation);
        Assert.That(records, Has.Count.EqualTo(1));
        var record = records.Single();
        using var details = JsonDocument.Parse(record.DetailsJson);
        Assert.Multiple(() =>
        {
            Assert.That(record.EventType, Is.EqualTo(AuditEventTypes.AuthLogin));
            Assert.That(record.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(record.ActorKind, Is.EqualTo(AuditActorKind.Anonymous));
            Assert.That(record.ActorId, Is.Null);
            Assert.That(record.ResourceType, Is.EqualTo(AuditResourceTypes.Account));
            Assert.That(record.ResourceId, Is.EqualTo(accountId.ToString()));
            Assert.That(record.CourseId, Is.Null);
            Assert.That(record.OccurredAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.That(details.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(details.RootElement.GetProperty("reasonCode").GetString(), Is.EqualTo("account_locked"));
            Assert.That(record.DetailsJson, Does.Not.Contain(email).And.Not.Contain(password));
        });
    }

    private WebApplicationFactory<Program> CreateIdentityHost(
        int loginPermitLimit = 1, int maxFailedAttempts = 5, ITrustedAuditWriter? writer = null) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("RateLimiting:Login:PermitLimit", loginPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
            builder.UseSetting("Identity:Lockout:MaxFailedAccessAttempts", maxFailedAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // Keep raw Audit configuration and the scoped context on the same restricted test role.
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Audit"] = auditRuntimeConnection
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AuditDbContext>();
                services.RemoveAll<DbContextOptions<AuditDbContext>>();
                services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(auditRuntimeConnection,
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
                // Restore the real writer hidden by the shared factory's default success stub.
                services.RemoveAll<ITrustedAuditWriter>();
                if (writer is null)
                    services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
                else
                    services.AddSingleton(writer);
            });
        });
}
