using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 the second factor (VerifyAsync): new Active session, replay and parallel requests.</summary>
[TestFixture]
[Category("US-18")]
public sealed class MfaServiceVerifyTests : MfaServiceTestBase
{
    [Test]
    public async Task Verify_WithCurrentCode_ReplacesPendingSessionWithNewActiveSession()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        // Act
        var (result, session) = await _service.VerifyAsync(Token, code, "corr-verify", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        VerifyConsumedStep(TotpHelpers.Verify(code, secret).timeStepMatched);
        _work.Verify(work => work.TryReplaceWithActiveSessionAsync(
            UserId, _sessionId, StoreSessionCreator.Lifetime, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Token, Is.EqualTo(NewToken));
        Assert.That(session.Token, Is.Not.EqualTo(Token));
        Assert.That(session.Handle, Is.EqualTo(_newActiveSession.SessionHandle));
        Assert.That(session.ExpiresAt, Is.EqualTo(_newActiveSession.ExpiresAt));
        VerifyCommitted(true);
    }

    [Test]
    public async Task Verify_WhenRefused_ReturnsNoSession()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (result, session) = await _service.VerifyAsync(Token, "000000", "corr-no-session", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(session, Is.Null);
        VerifyReplaced(Times.Never());
    }

    [Test]
    public async Task Verify_WithOlderStepThanTheLastUsedOne_IsRejectedWithoutTransaction()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);
        var matched = TotpHelpers.Verify(code, secret).timeStepMatched;
        // A newer code (next step) was already used; the current one must not be accepted after it.
        SetupVerification(secret, current, currentStep: matched + 1);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-older", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_WhenParallelRequestConsumedTheStepFirst_IsRejectedAndSessionStaysPending()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _work.Setup(work => work.TryConsumeTimeStepAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-race", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyReplaced(Times.Never());
        VerifyCommitted(false);
        VerifyDisposed();
    }

    [Test]
    public async Task Verify_WhenSessionWasRevokedMeanwhile_RollsBackTheConsumedStep()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _work.Setup(work => work.TryReplaceWithActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string, Session)?)null);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-revoked", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyCommitted(false);
        VerifyDisposed();
    }

    [Test]
    public async Task Verify_WhenSessionOrAccountIsNoLongerActiveAtTheWrite_IsUnauthorized_AndConsumesNothing()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _work.Setup(work => work.TryLockActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AuthLevel>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-verify-blocked", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyLockedSession(AuthLevel.MfaPending);
        VerifyNoStepConsumed();
        VerifyReplaced(Times.Never());
        VerifyCommitted(false);
        VerifyAuditDoesNotContainSecret("session_inactive", AuditEventTypes.AuthMfaVerified);
    }

    [Test]
    public async Task Verify_WithWrongCode_RecordsFailureWithoutPromotingSession()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (result, _) = await _service.VerifyAsync(Token, "000000", "corr-wrong", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyAuditDoesNotContainSecret("invalid_code", AuditEventTypes.AuthMfaVerified);
        _credentials.Verify(store => store.RecordFailedAttemptAsync(
            UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_WithReplay_IsRejected()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);
        var matched = TotpHelpers.Verify(code, secret).timeStepMatched;
        SetupVerification(secret, current, currentStep: matched);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (result, _) = await _service.VerifyAsync(Token, code, "corr-replay", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyAuditDoesNotContainSecret("replayed_code", AuditEventTypes.AuthMfaVerified);
    }
}
