using Microsoft.EntityFrameworkCore;
using Moq;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.SessionSecurity.Sessions;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;
using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Identity.Tests.Integration.Persistence.Sessions;

/// <summary>
/// IDN-11: <see cref="IdentitySessionRotationStore"/> over the real identity.sessions table (PostgreSQL,
/// Testcontainers). Proves that the Team 1 session check rejects the old token and accepts the new one,
/// that a rotation is atomic, keeps the session handle so IDN-07 revocation still ends the session (D4),
/// never extends the lifetime (D6), never rotates a blocked account (IDN-03) and locks the account before
/// the session (D13), and that the fail-closed cleanup revokes only the rotated session and keeps the other
/// sessions of the subject (D4, D5, AUD-10, IDN-07).
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("IDN-11")]
public sealed class IdentitySessionRotationStoreTests : IntegrationTestBase
{
    private const string Password = "Password123!";
    private const string UserA = "user-a@example.com";
    private const string UserB = "user-b@example.com";
    private const string CorrelationId = "corr-idn11-store";
    private const string MalformedHash = "not-a-hex-hash";

    private static readonly TimeSpan ShortLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PostgresTimestampPrecision = TimeSpan.FromMicroseconds(1);
    private static readonly CryptoSessionTokenGenerator TokenGenerator = new();

    // Rejections: nothing is rotated and the stored sessions stay as they were.

    [Test]
    public async Task FindAsync_MalformedHash_ShouldReturnNull()
    {
        // Arrange
        var store = GetService<ISessionRotationStore>();

        // Act
        var found = await store.FindAsync(MalformedHash);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task RotateAsync_MalformedHash_ShouldReturnNotFound_AndKeepSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now());

        // Act
        var status = await GetService<ISessionRotationStore>().RotateAsync(MalformedHash, replacement, Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RotateAsync_RevokedSession_ShouldReturnAlreadyRevoked_AndRejectNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);
        var issued = TokenGenerator.Generate();

        // Act
        var status = await GetService<ISessionRotationStore>().RotateAsync(
            seeded.Current.TokenHash, ReplacementFor(seeded.Current, issued, Now()), Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_ExpiredSession_ShouldReturnExpired_AndKeepSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA, lifetime: ShortLifetime);
        var afterExpiry = seeded.Current.AbsoluteExpiresAt;
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), afterExpiry);

        // Act
        var status = await GetService<ISessionRotationStore>().RotateAsync(
            seeded.Current.TokenHash, replacement, afterExpiry);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Expired));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That((await FindAsync(seeded.RawToken))!.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_BlockedAccount_ShouldReturnAlreadyRevoked_AndStoreNothing()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA, AccountStatus.Blocked);
        var issued = TokenGenerator.Generate();

        // Act
        var status = await GetService<ISessionRotationStore>().RotateAsync(
            seeded.Current.TokenHash, ReplacementFor(seeded.Current, issued, Now()), Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await FindAsync(issued.Value), Is.Null);
            Assert.That((await FindAsync(seeded.RawToken))!.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_BlockingHoldsAccountLock_ShouldReturnAlreadyRevoked_AndLeaveNoActiveSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        var replacement = ReplacementFor(seeded.Current, issued, Now());
        await using var block = await GetService<IAccountStatusStore>().BeginAsync();
        var account = await block.FindForUpdateAsync(seeded.UserId);
        // Not null: the account was seeded above and nothing deletes it.
        await block.ApplyAsync(account!, AccountStatus.Blocked);

        // Act: the rotation waits for the account lock (D13) until the block commits.
        var rotation = GetService<ISessionRotationStore>().RotateAsync(seeded.Current.TokenHash, replacement, Now());
        await block.CommitAsync();
        var status = await rotation;

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await FindAsync(issued.Value), Is.Null);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RotateAsync_ChainIdMismatch_ShouldThrow_AndKeepOldToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now()) with
        {
            ChainId = Guid.NewGuid(),
        };
        var store = GetService<ISessionRotationStore>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RotateAsync(seeded.Current.TokenHash, replacement, Now()));
        Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
    }

    [Test]
    public async Task RotateAsync_ReplacementExtendsExpiry_ShouldThrow_AndKeepOldToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now()) with
        {
            AbsoluteExpiresAt = seeded.Current.AbsoluteExpiresAt.AddHours(1),
        };
        var store = GetService<ISessionRotationStore>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RotateAsync(seeded.Current.TokenHash, replacement, Now()));
        Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
    }

    [Test]
    public async Task RotateAsync_OldTokenAlreadyRotated_ShouldReturnNotFound_AndKeepNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        // Act
        var status = await RotateAsync(seeded.Current, TokenGenerator.Generate());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.True);
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ActivateAsync_RevokedSession_ShouldReturnFalse()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);

        // Act
        var activated = await GetService<ISessionRotationStore>().ActivateAsync(seeded.Current.TokenHash, Now());

        // Assert
        Assert.That(activated, Is.False);
    }

    [Test]
    public async Task RevokeChainAsync_UnknownChain_ShouldReturnZero_AndKeepSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var revoked = await GetService<ISessionRotationStore>().RevokeChainAsync(Guid.NewGuid(), Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.Zero);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RevokeAllForSubjectAsync_SubjectIsNotGuid_ShouldReturnZero_AndKeepSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var revoked = await GetService<ISessionRotationStore>().RevokeAllForSubjectAsync("user-a", Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.Zero);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RevokeChainAsync_OtherSubjectHasSession_ShouldKeepOtherSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var other = await SeedSessionAsync(UserB);

        // Act
        await GetService<ISessionRotationStore>().RevokeChainAsync(seeded.Current.ChainId, Now());

        // Assert
        Assert.That(await IsAcceptedBySessionCheckAsync(other.RawToken), Is.True);
    }

    // IDN-07: the handle listed before a rotation still ends the session after it and during it.

    [Test]
    public async Task RevokeSession_HandleListedBeforeRotation_ShouldReturnTrue_AndRejectNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        // Act
        var revoked = await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RotateAsync_RevokedByHandleDuringRotation_ShouldLeaveNoActiveSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        // Act
        var rotation = RotateAsync(seeded.Current, issued);
        var revocation = RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);
        await Task.WhenAll(rotation, revocation);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await rotation, Is.AnyOf(SessionRotationStoreStatus.Rotated, SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await revocation, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    // Rotation of an active session, as seen by the Team 1 session check.

    [Test]
    public void GetService_ApiHost_ShouldResolveIdentitySessionRotationStore()
    {
        // Act
        var store = GetService<ISessionRotationStore>();

        // Assert
        Assert.That(store, Is.InstanceOf<IdentitySessionRotationStore>());
    }

    [Test]
    public async Task FindAsync_ActiveSession_ShouldMapIdentitySession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, UserA, Password, AccountStatus.Active);
        var (rawToken, session) = await SessionTestData.CreateAsync(Factory, user.Id);

        // Act
        var found = await FindAsync(rawToken);

        // Assert
        Assert.That(found, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(found!.ChainId, Is.EqualTo(session.SessionHandle));
            Assert.That(found.SubjectId, Is.EqualTo(user.Id.ToString("D")));
            Assert.That(found.TokenHash, Is.EqualTo(TokenGenerator.ComputeHash(rawToken)));
            Assert.That(found.PrivilegeLevel, Is.EqualTo(SessionPrivilegeLevel.Authenticated));
            // PostgreSQL keeps microseconds; the in-memory entity still has the full .NET precision.
            Assert.That(found.AbsoluteExpiresAt, Is.EqualTo(session.ExpiresAt).Within(PostgresTimestampPrecision));
            Assert.That(found.RevokedAt, Is.Null);
            Assert.That(found.IsPendingActivation, Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_ActiveSession_ShouldReturnRotated_AndRejectOldToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var status = await RotateAsync(seeded.Current, TokenGenerator.Generate());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Rotated));
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_ActiveSession_ShouldAcceptNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        // Act
        await RotateAsync(seeded.Current, issued);

        // Assert
        Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.True);
    }

    [Test]
    public async Task RotateAsync_ActiveSession_ShouldKeepAbsoluteExpiry()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        // Act
        await RotateAsync(seeded.Current, issued);

        // Assert
        Assert.That((await FindAsync(issued.Value))!.AbsoluteExpiresAt, Is.EqualTo(seeded.Current.AbsoluteExpiresAt));
    }

    [Test]
    public async Task RotateAsync_ActiveSession_ShouldKeepHandleListedForSessionReview()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        await RotateAsync(seeded.Current, TokenGenerator.Generate());

        // Assert
        var listed = await GetService<ISessionManager>().ListForUser(seeded.UserId);
        Assert.That(listed.Select(session => session.Handle), Is.EqualTo(new[] { seeded.Session.SessionHandle }));
    }

    [Test]
    public async Task ActivateAsync_ActiveSession_ShouldReturnTrue()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var activated = await GetService<ISessionRotationStore>().ActivateAsync(seeded.Current.TokenHash, Now());

        // Assert
        Assert.That(activated, Is.True);
    }

    [Test]
    public async Task RotateAsync_TwoRequestsWithSameOldToken_ShouldRotateExactlyOnce()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var statuses = await Task.WhenAll(
            RotateAsync(seeded.Current, TokenGenerator.Generate()),
            RotateAsync(seeded.Current, TokenGenerator.Generate()));

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(statuses.Count(status => status == SessionRotationStoreStatus.Rotated), Is.EqualTo(1));
            // The loser waits on the row lock and sees the new hash (AlreadyRevoked), or starts after the
            // commit and no longer finds the old hash (NotFound). The client gets the same answer for both.
            Assert.That(
                statuses.Single(status => status != SessionRotationStoreStatus.Rotated),
                Is.AnyOf(SessionRotationStoreStatus.AlreadyRevoked, SessionRotationStoreStatus.NotFound));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotateAsync_AccountBlockedDuringRotation_ShouldLeaveNoActiveSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);

        // Act
        var rotation = RotateAsync(seeded.Current, TokenGenerator.Generate());
        var block = BlockAccountAsync(seeded.UserId);
        await Task.WhenAll(rotation, block);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await rotation, Is.AnyOf(SessionRotationStoreStatus.Rotated, SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RevokeChainAsync_SubjectHasRotatedAndOtherSessions_ShouldRevokeOnlyRotatedSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        // Act
        var revoked = await GetService<ISessionRotationStore>().RevokeChainAsync(seeded.Current.ChainId, Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.True);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RevokeChainAsync_ChainAlreadyRevoked_ShouldReturnZero_AndKeepOtherSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);

        // Act
        var revoked = await GetService<ISessionRotationStore>().RevokeChainAsync(seeded.Current.ChainId, Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.Zero);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.True);
        });
    }

    [Test]
    public async Task RevokeAllForSubjectAsync_RotatedSession_ShouldRevokeIt()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        // Act
        var revoked = await GetService<ISessionRotationStore>().RevokeAllForSubjectAsync(
            seeded.UserId.ToString("D"), Now());

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
        });
    }

    // End to end with the real SessionRotationService; only the audit writer is a mock.

    [Test]
    public async Task RotationService_ActiveSession_ShouldRejectOldToken_AndAcceptNewToken()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var entries = new List<SessionSecurityAuditEntry>();
        var audit = AuditWriter(writeSucceeds: true, entries);

        // Act
        var result = await RotateThroughServiceAsync(seeded, audit.Object);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            // Not null: a successful result always carries the new token.
            Assert.That(await IsAcceptedBySessionCheckAsync(result.NewToken!), Is.True);
            Assert.That(entries.Single().Outcome, Is.EqualTo(SessionRotationCodes.Rotated));
        });
    }

    [Test]
    public async Task RotationService_AuditFails_ShouldRevokeOnlyRotatedSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        var audit = AuditWriter(writeSucceeds: false);

        // Act
        var result = await RotateThroughServiceAsync(seeded, audit.Object);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.True);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotationService_RevokedByHandleBeforeActivation_ShouldKeepOtherSessions()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        var entries = new List<SessionSecurityAuditEntry>();
        var audit = new Mock<ISessionSecurityAuditWriter>(MockBehavior.Strict);
        // The rotation is audited after RotateAsync and before ActivateAsync; at that moment the user
        // revokes the session by its handle from another device (IDN-07).
        audit
            .Setup(w => w.TryWriteAsync(It.IsAny<SessionSecurityAuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns<SessionSecurityAuditEntry, CancellationToken>(async (entry, _) =>
            {
                entries.Add(entry);
                if (entry.Outcome == SessionRotationCodes.Rotated)
                {
                    await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);
                }

                return true;
            });

        // Act
        var result = await RotateThroughServiceAsync(seeded, audit.Object);

        // Assert
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.RotationConflict));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(
                entries.Select(entry => entry.Outcome),
                Is.EqualTo(new[] { SessionRotationCodes.Rotated, SessionRotationCodes.RotationConflict }));
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.True);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotationService_CancelledAfterRotation_ShouldRevokeOnlyRotatedSession()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(Factory, seeded.UserId);
        var audit = new Mock<ISessionSecurityAuditWriter>(MockBehavior.Strict);
        // The HTTP request is aborted after the rotation committed, while the rotation is audited.
        audit
            .Setup(w => w.TryWriteAsync(It.IsAny<SessionSecurityAuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromException<bool>(new OperationCanceledException()));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await RotateThroughServiceAsync(seeded, audit.Object));
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.True);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotationService_ActiveSession_ShouldWriteAuditWithoutTokenHashOrHandle()
    {
        // Arrange
        var seeded = await SeedSessionAsync(UserA);
        var entries = new List<SessionSecurityAuditEntry>();
        var audit = AuditWriter(writeSucceeds: true, entries);

        // Act
        var result = await RotateThroughServiceAsync(seeded, audit.Object);

        // Assert
        var recorded = entries.Single().ToString();
        audit.Verify(
            writer => writer.TryWriteAsync(It.IsAny<SessionSecurityAuditEntry>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(recorded, Does.Not.Contain(seeded.RawToken));
            Assert.That(recorded, Does.Not.Contain(seeded.Current.TokenHash));
            Assert.That(recorded, Does.Not.Contain(seeded.Session.SessionHandle.ToString()));
            // Not null: the rotation succeeded, see the end-to-end test above.
            Assert.That(recorded, Does.Not.Contain(result.NewToken!));
            Assert.That(recorded, Does.Not.Contain(TokenGenerator.ComputeHash(result.NewToken!)));
        });
    }

    private async Task<SeededSession> SeedSessionAsync(
        string email,
        AccountStatus status = AccountStatus.Active,
        TimeSpan? lifetime = null)
    {
        var user = await UserTestData.AddAsync(Factory, email, Password, status);
        var (rawToken, session) = await SessionTestData.CreateAsync(Factory, user.Id, lifetime: lifetime);
        // Not null: the session was stored just above.
        var current = (await FindAsync(rawToken))!;
        return new SeededSession(user.Id, rawToken, session, current);
    }

    private static RotatableSession ReplacementFor(
        RotatableSession current,
        IssuedSessionToken issued,
        DateTimeOffset now) =>
        new(current.ChainId, current.SubjectId, issued.Hash, SessionPrivilegeLevel.Authenticated,
            now, current.AbsoluteExpiresAt, IsPendingActivation: true);

    // Every call gets its own scope and DbContext, so parallel calls are separate transactions.
    private static async Task<SessionRotationStoreStatus> RotateAsync(RotatableSession current, IssuedSessionToken issued) =>
        await GetService<ISessionRotationStore>().RotateAsync(
            current.TokenHash, ReplacementFor(current, issued, Now()), Now());

    private static async Task<SessionRotationResult> RotateThroughServiceAsync(
        SeededSession seeded,
        ISessionSecurityAuditWriter audit)
    {
        var service = new SessionRotationService(
            GetService<ISessionRotationStore>(), TokenGenerator, audit, GetService<TimeProvider>());

        return await service.RotateAsync(new RotateSessionRequest(
            seeded.RawToken, seeded.UserId.ToString("D"), RotationTrigger.MfaCompleted,
            SessionPrivilegeLevel.Elevated, CorrelationId));
    }

    private static Mock<ISessionSecurityAuditWriter> AuditWriter(
        bool writeSucceeds,
        List<SessionSecurityAuditEntry>? entries = null)
    {
        var writer = new Mock<ISessionSecurityAuditWriter>(MockBehavior.Strict);
        writer
            .Setup(w => w.TryWriteAsync(It.IsAny<SessionSecurityAuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<SessionSecurityAuditEntry, CancellationToken>((entry, _) => entries?.Add(entry))
            .Returns(() => ValueTask.FromResult(writeSucceeds));
        return writer;
    }

    private static async Task<RotatableSession?> FindAsync(string rawToken) =>
        await GetService<ISessionRotationStore>().FindAsync(TokenGenerator.ComputeHash(rawToken));

    private static async Task<bool> IsAcceptedBySessionCheckAsync(string rawToken) =>
        await GetService<ISessionManager>().ValidateAsync(rawToken) is not null;

    private static Task<bool> RevokeWithSessionManagerAsync(Guid userId, Guid sessionHandle) =>
        GetService<ISessionManager>().RevokeSession(userId, sessionHandle);

    private static async Task BlockAccountAsync(Guid userId)
    {
        await using var block = await GetService<IAccountStatusStore>().BeginAsync();
        var account = await block.FindForUpdateAsync(userId);
        // Not null: the account was seeded by the calling test and nothing deletes it.
        await block.ApplyAsync(account!, AccountStatus.Blocked);
        await block.CommitAsync();
    }

    private static Task<int> CountSessionsAsync(Guid userId) =>
        GetService<PlatformIdentityDbContext>().Sessions.CountAsync(session => session.UserId == userId);

    private static Task<int> CountActiveSessionsAsync(Guid userId)
    {
        var now = Now();
        return GetService<PlatformIdentityDbContext>().Sessions.CountAsync(session =>
            session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now);
    }

    private static DateTimeOffset Now() => GetService<TimeProvider>().GetUtcNow();

    private sealed record SeededSession(Guid UserId, string RawToken, Session Session, RotatableSession Current)
    {
        // AUD-03: never print the session token, its hash or the handle in test output.
        public override string ToString() => $"SeededSession {{ UserId = {UserId} }}";
    }
}
