using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Api.Enums.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class IdentityAuditPostgresIntegrationTests
{
    private const string Password = "Strong-password!2026";

    private PostgreSqlContainer database = null!;
    private string auditRuntimeConnection = null!;
    private DbContextOptions<PlatformIdentityDbContext> identityOptions = null!;
    private DbContextOptions<AuditDbContext> auditOptions = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();

        identityOptions = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    PlatformIdentityDbContext.SchemaName))
            .Options;
        auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;

        await using var identity = new PlatformIdentityDbContext(identityOptions);
        await identity.Database.MigrateAsync();
        await using var audit = new AuditDbContext(auditOptions);
        await audit.Database.MigrateAsync();
        var password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        // The generated password is hexadecimal; this role exists only in the disposable container.
        await using var command = new NpgsqlCommand($"""
            CREATE ROLE s16_audit_runtime LOGIN PASSWORD '{password}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            GRANT USAGE ON SCHEMA audit TO s16_audit_runtime;
            GRANT SELECT, INSERT ON audit.audit_events TO s16_audit_runtime;
            """, connection);
        await command.ExecuteNonQueryAsync();
        auditRuntimeConnection = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            Username = "s16_audit_runtime", Password = password, Pooling = false
        }.ConnectionString;
    }

    [OneTimeTearDown]
    public async Task StopDatabase() => await database.DisposeAsync();

    [Test]
    public async Task LoginAndLogoutHttpFlow_PersistsTrustedAuditEventsAndRevokesSession()
    {
        var userId = Guid.NewGuid();
        var email = $"audit-flow-{userId:N}@example.com";
        await SeedAccountAsync(userId, email);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = database.GetConnectionString(),
                    ["ConnectionStrings:Audit"] = auditRuntimeConnection
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformIdentityDbContext>();
                services.RemoveAll<DbContextOptions<PlatformIdentityDbContext>>();
                services.AddDbContext<PlatformIdentityDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString(), postgres =>
                        postgres.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            PlatformIdentityDbContext.SchemaName)));

                services.RemoveAll<AuditDbContext>();
                services.RemoveAll<DbContextOptions<AuditDbContext>>();
                services.AddDbContext<AuditDbContext>(options =>
                    options.UseNpgsql(auditRuntimeConnection, postgres =>
                        postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = Password });
        using var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);

        await using var freshIdentity = new PlatformIdentityDbContext(identityOptions);
        await using var freshAudit = new AuditDbContext(auditOptions);
        var storedSession = await freshIdentity.Sessions.AsNoTracking()
            .SingleAsync(session => session.UserId == userId);
        var events = await freshAudit.Events.AsNoTracking()
            .Where(row => row.ActorId == userId)
            .Select(row => row.EventType)
            .ToListAsync();

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
    public async Task Login_WhenAuditWriteFails_PersistsOnlyARevokedSession()
    {
        var userId = Guid.NewGuid();
        var email = $"audit-down-{userId:N}@example.com";
        await SeedAccountAsync(userId, email);

        await using var identity = new PlatformIdentityDbContext(identityOptions);
        var sessionStore = new SessionStore(identity, TimeProvider.System);
        var login = new LoginService(
            new CredentialVerifier(
                new EfCredentialStore(identity),
                new BCryptPasswordHasher(),
                Options.Create(new IdentityLockoutOptions()),
                TimeProvider.System),
            new StoreSessionCreator(sessionStore, NullLogger<StoreSessionCreator>.Instance),
            new SessionManager(identity),
            CreateProducer(new UnavailableAuditWriter()));

        var result = await login.ExecuteAsync(email, Password, Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(result.Session, Is.Null);

        await using var freshIdentity = new PlatformIdentityDbContext(identityOptions);
        var storedSession = await freshIdentity.Sessions.AsNoTracking()
            .SingleAsync(session => session.UserId == userId);
        Assert.That(storedSession.RevokedAt, Is.Not.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ThrottledLogin_PersistsOneAnonymousDeniedEvent_WithNoCredentials(bool knownAccount)
    {
        var userId = Guid.NewGuid();
        var email = $"throttled-{userId:N}@example.com";
        if (knownAccount) await SeedAccountAsync(userId, email);
        await using var factory = CreateThrottledHost();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(first.StatusCode, Is.EqualTo(knownAccount ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));

        // Even a previously issued cookie and forged headers cannot assign an actor to throttling.
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "client-correlation");
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var correlation = blocked.Headers.GetValues("X-Correlation-ID").Single();
        var body = await blocked.Content.ReadAsStringAsync();

        await using var freshAudit = new AuditDbContext(auditOptions);
        var records = await freshAudit.Events.AsNoTracking().Where(row => row.CorrelationId == correlation).ToListAsync();
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
        await using var freshIdentity = new PlatformIdentityDbContext(identityOptions);
        Assert.That(await freshIdentity.Sessions.CountAsync(session => session.UserId == userId),
            Is.EqualTo(knownAccount ? 1 : 0), "The rejected request must never issue another session.");
    }

    [Test]
    public async Task ThrottledLogin_WhenDatabaseRejectsInsert_StillReturns429WithoutSession()
    {
        var email = $"no-audit-{Guid.NewGuid():N}@example.com";
        await using var factory = CreateThrottledHost();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        await ExecuteAuditSqlAsync("REVOKE INSERT ON audit.audit_events FROM s16_audit_runtime");
        try
        {
            using var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            var correlation = blocked.Headers.GetValues("X-Correlation-ID").Single();
            Assert.Multiple(() =>
            {
                Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That(blocked.Headers.RetryAfter, Is.Not.Null);
                Assert.That(blocked.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(blocked.Headers.Contains("Set-Cookie"), Is.False);
            });
            await using var audit = new AuditDbContext(auditOptions);
            Assert.That(await audit.Events.AnyAsync(row => row.CorrelationId == correlation), Is.False);
            Assert.That(await blocked.Content.ReadAsStringAsync(), Does.Contain("rate_limited"));
        }
        finally
        {
            await ExecuteAuditSqlAsync("GRANT INSERT ON audit.audit_events TO s16_audit_runtime");
        }
    }

    private async Task ExecuteAuditSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private WebApplicationFactory<Program> CreateThrottledHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("RateLimiting:Login:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = database.GetConnectionString(),
                    ["ConnectionStrings:Audit"] = auditRuntimeConnection
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformIdentityDbContext>();
                services.RemoveAll<DbContextOptions<PlatformIdentityDbContext>>();
                services.AddDbContext<PlatformIdentityDbContext>(options => options.UseNpgsql(database.GetConnectionString(),
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName)));
                services.RemoveAll<AuditDbContext>();
                services.RemoveAll<DbContextOptions<AuditDbContext>>();
                services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(auditRuntimeConnection,
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
            });
        });

    private async Task SeedAccountAsync(Guid userId, string email)
    {
        var account = new UserAccount(userId, CredentialVerifier.NormalizeEmail(email));
        account.SetPasswordHash(new BCryptPasswordHasher().Hash(Password));

        await using var context = new PlatformIdentityDbContext(identityOptions);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
    }

    private static IdentityAuditProducer CreateProducer(ITrustedAuditWriter writer) =>
        new(writer, NullLogger<IdentityAuditProducer>.Instance);

    private sealed class UnavailableAuditWriter : ITrustedAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditWriteResult.Unavailable());
    }
}
