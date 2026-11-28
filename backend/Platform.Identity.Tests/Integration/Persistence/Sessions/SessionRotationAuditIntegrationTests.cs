using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Platform.Api.Interfaces.Identity;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Tests.Helpers;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Identity.Tests.Integration.Persistence.Sessions;

/// <summary>
/// IDN-11 / AUD-01 / AUD-10: a session rotation through the host's <see cref="IRotateSessionUseCase"/>, with the
/// real <c>IdentitySessionRotationStore</c> over identity.sessions and the real audit adapter. A successful
/// rotation replaces the token in identity.sessions, so the Team 1 session check rejects the old token, and writes
/// one session.rotated row without the token, its hash or the session handle (AUD-03). When the audit write fails,
/// the rotation fails closed: no new token is issued and the rotated session is revoked, while the other sessions
/// of the user stay valid (D4, IDN-07).
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("IDN-11")]
public sealed class SessionRotationAuditIntegrationTests : IntegrationTestBase
{
    private const string Password = "synthetic-password-2026";
    private const string UserA = "user-a@example.com";
    private const string CorrelationId = "corr-idn11-rotation-audit";

    private static readonly CryptoSessionTokenGenerator TokenGenerator = new();

    [OneTimeSetUp]
    public async Task MigrateAuditSchema() =>
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);

    [Test]
    public async Task Rotate_ActiveSession_ShouldRejectOldToken_AndAcceptNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        using var host = HostWithRealAudit();

        // Act
        var result = await RotateAsync(host, seeded);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            // Not null: a successful result always carries the new token.
            Assert.That(await IsAcceptedBySessionCheckAsync(result.NewToken!), Is.True);
        });
    }

    [Test]
    public async Task Rotate_ActiveSession_ShouldWriteOneSuccessRow_WithLoginTrigger()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        using var host = HostWithRealAudit();

        // Act
        await RotateAsync(host, seeded);

        // Assert
        var row = (await RotationRowsAsync(seeded.UserId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Outcome, Is.EqualTo("Success"));
            Assert.That(row.ActorKind, Is.EqualTo("User"));
            Assert.That(row.ActorId, Is.EqualTo(seeded.UserId));
            Assert.That(row.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(row.Raw, Does.Contain("\"trigger\"").And.Contain("\"login\""));
            Assert.That(row.Raw, Does.Not.Contain("reasonCode"));
        });
    }

    [Test]
    public async Task Rotate_ActiveSession_ShouldStoreNoTokenHashOrHandleInAudit()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        using var host = HostWithRealAudit();

        // Act
        var result = await RotateAsync(host, seeded);

        // Assert
        var raw = (await RotationRowsAsync(seeded.UserId)).Single().Raw;
        // Not null: the rotation succeeded, see the test above.
        var newToken = result.NewToken!;
        Assert.Multiple(() =>
        {
            Assert.That(raw, Does.Not.Contain(seeded.RawToken));
            Assert.That(raw, Does.Not.Contain(newToken));
            Assert.That(raw, Does.Not.Contain(TokenGenerator.ComputeHash(seeded.RawToken)).IgnoreCase);
            Assert.That(raw, Does.Not.Contain(TokenGenerator.ComputeHash(newToken)).IgnoreCase);
            Assert.That(raw, Does.Not.Contain(seeded.Session.SessionHandle.ToString()).IgnoreCase);
        });
    }

    [Test]
    public async Task Rotate_RevokedSession_ShouldReturnSessionRevoked_AndWriteDeniedRow()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        await GetService<ISessionManager>().RevokeSession(seeded.UserId, seeded.Session.SessionHandle);
        using var host = HostWithRealAudit();

        // Act
        var result = await RotateAsync(host, seeded);

        // Assert
        var row = (await RotationRowsAsync(seeded.UserId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(row.Outcome, Is.EqualTo("Denied"));
            Assert.That(row.Raw, Does.Contain("\"session_revoked\""));
        });
    }

    [Test]
    public async Task Rotate_AuditUnavailable_ShouldReturnAuditFailed_AndIssueNoToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        using var host = HostWithAuditWriter(UnavailableAuditWriter());

        // Act
        var result = await RotateAsync(host, seeded);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
        });
    }

    [Test]
    public async Task Rotate_AuditUnavailable_ShouldRevokeRotatedSession_AndKeepOtherSessions()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        var (otherToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        using var host = HostWithAuditWriter(UnavailableAuditWriter());

        // Act
        await RotateAsync(host, seeded);

        // Assert
        var rotated = await SessionTestData.GetAsync(Factory, seeded.Session.Id);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(rotated?.RevokedAt, Is.Not.Null);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(otherToken), Is.True);
            Assert.That(await RotationRowsAsync(seeded.UserId), Is.Empty);
        });
    }

    [Test]
    public async Task Rotate_AuditWriterThrows_ShouldReturnAuditFailed_AndRevokeRotatedSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync();
        var writer = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        writer
            .Setup(w => w.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit store offline"));
        using var host = HostWithAuditWriter(writer.Object);

        // Act
        var result = await RotateAsync(host, seeded);

        // Assert
        var rotated = await SessionTestData.GetAsync(Factory, seeded.Session.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(rotated?.RevokedAt, Is.Not.Null);
        });
    }

    private async Task<SeededSession> SeedSessionAsync()
    {
        var user = await UserTestData.AddAsync(Factory, UserA, Password);
        var (rawToken, session) = await SessionTestData.CreateAsync(Factory, user.Id);
        return new SeededSession(user.Id, rawToken, session);
    }

    // AUD-01: the shared factory replaces ITrustedAuditWriter with a writer that stores nothing. These tests read the
    // real session.rotated rows, so the real writer is restored; ConnectionStrings:Audit is the test database.
    private static WebApplicationFactory<Program> HostWithRealAudit() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        }));

    private static WebApplicationFactory<Program> HostWithAuditWriter(ITrustedAuditWriter writer) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddSingleton(writer);
        }));

    private static ITrustedAuditWriter UnavailableAuditWriter()
    {
        var writer = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        writer
            .Setup(w => w.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuditWriteResult.Unavailable());
        return writer.Object;
    }

    // The use case, the session store and the audit adapter all come from the host's DI, in one request scope.
    private static async Task<SessionRotationResult> RotateAsync(
        WebApplicationFactory<Program> host,
        SeededSession seeded)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var rotation = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();
        return await rotation.RotateAsync(new RotateSessionRequest(
            seeded.RawToken, seeded.UserId.ToString("D"), RotationTrigger.Login,
            SessionPrivilegeLevel.Authenticated, CorrelationId));
    }

    private static async Task<bool> IsAcceptedBySessionCheckAsync(string rawToken) =>
        await GetService<ISessionManager>().ValidateAsync(rawToken) is not null;

    private static Task<List<AuditTestData.Row>> RotationRowsAsync(Guid userId) =>
        AuditTestData.RowsAsync(userId, AuditEventTypes.SessionRotated);

    private sealed record SeededSession(Guid UserId, string RawToken, Session Session)
    {
        // AUD-03: never print the session token or the handle in test output.
        public override string ToString() => $"SeededSession {{ UserId = {UserId} }}";
    }
}
