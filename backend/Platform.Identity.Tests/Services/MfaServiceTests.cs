using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-17")]
public sealed class MfaServiceTests
{
    private const string Token = "session-token";
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);
    private Mock<ISessionStore> _sessions = null!;
    private Mock<IMfaCredentialsStore> _credentials = null!;
    private RecordingAudit _audit = null!;
    private Mock<TimeProvider> _clock = null!;
    private ITotpSecretProtector _protector = null!;
    private FakeUnitOfWork _work = null!;
    private MfaService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        _credentials = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        _audit = new RecordingAudit();
        _clock = new Mock<TimeProvider>(MockBehavior.Strict);
        _clock.Setup(clock => clock.GetUtcNow()).Returns(_now);
        _work = new FakeUnitOfWork();
        _credentials.Setup(store => store.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_work);
        _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        _service = new MfaService(_credentials.Object, _sessions.Object,
            new IdentityAuditProducer(_audit, _audit, NullLogger<IdentityAuditProducer>.Instance),
            _clock.Object, _protector, Options.Create(new IdentityLockoutOptions()), NullLogger<MfaService>.Instance);
    }

    [Test]
    public async Task Enroll_WithoutToken_IsUnauthorized_AndWritesNoAudit()
    {
        var result = await _service.EnrollAsync(null, "corr-1", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_audit.Writes, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithMfaPendingSession_IsUnauthorized()
    {
        SetupSession(AuthLevel.MfaPending);

        var result = await _service.EnrollAsync(Token, "corr-2", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        _credentials.Verify(store => store.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Enroll_WhenMfaIsAlreadyEnabled_ReturnsConflict_AndDoesNotReplaceSecret()
    {
        SetupSession();
        var credentials = new MfaCredentials(UserId)
        {
            MfaEnabled = true,
            TotpSecretCiphertext = "existing-ciphertext",
            TotpSecretKeyVersion = 1
        };
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credentials);

        var result = await _service.EnrollAsync(Token, "corr-3", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.AlreadyEnabled));
        Assert.That(result.Uri, Is.Null);
        _credentials.Verify(store => store.CreatePendingAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_audit.Writes, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithActiveSession_CreatesPendingEnrollment_AndReturnsTotpUri()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _credentials.Setup(store => store.CreatePendingAsync(
                UserId, It.IsAny<string>(), 1, _now.AddMinutes(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.EnrollAsync(Token, "corr-success", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Success));
        Assert.That(result.Uri, Does.StartWith("otpauth://totp/"));
        Assert.That(result.Uri, Does.Contain("student%40example.test"));
        _credentials.Verify(store => store.CreatePendingAsync(
            UserId, It.Is<string>(ciphertext => !string.IsNullOrWhiteSpace(ciphertext)),
            1, _now.AddMinutes(10), It.IsAny<CancellationToken>()), Times.Once);
        var started = _audit.Writes.Single();
        Assert.That(started.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnrollmentStarted));
        Assert.That(started.Transaction, Is.Null);
        Assert.That(started.Context.Actor, Is.EqualTo(AuditActor.ForUser(UserId)));
    }

    [Test]
    public async Task Enroll_WithUnexpiredPendingEnrollment_IsRejected()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                PendingTotpSecretCiphertext = "pending-ciphertext",
                PendingTotpSecretKeyVersion = 1,
                PendingEnrollmentExpiresAt = _now.AddMinutes(5)
            });
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _credentials.Setup(store => store.CreatePendingAsync(
                UserId, It.IsAny<string>(), 1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.EnrollAsync(Token, "corr-4", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.EnrollmentPending));
        Assert.That(result.Uri, Is.Null);
        _credentials.Verify(store => store.CreatePendingAsync(
            UserId, It.IsAny<string>(), 1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Confirm_WithWrongCode_DoesNotEnableMfa()
    {
        SetupSession();
        var secret = TotpHelpers.GenerateSecret();
        SetupPendingCredentials(_protector.Protect(secret));

        var result = await _service.ConfirmEnrollAsync(Token, "000000", "corr-5", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(_work.ConfirmCalls, Is.Zero);
        VerifyAuditDoesNotContainSecret("invalid_code");
    }

    [Test]
    public async Task Confirm_WithCurrentCode_EnablesMfa()
    {
        var current = DateTimeOffset.UtcNow;
        _clock.Setup(clock => clock.GetUtcNow()).Returns(current);
        SetupSession(now: current);
        var secret = TotpHelpers.GenerateSecret();
        SetupPendingCredentials(_protector.Protect(secret), current.AddMinutes(5));
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-confirm", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        Assert.That(_work.ConfirmCalls, Is.EqualTo(1));
        Assert.That(_work.Committed, Is.True);
    }

    [Test]
    public async Task Confirm_WithPreviousTotpPeriod_EnablesMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(current.UtcDateTime.AddSeconds(-30));

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-previous-period", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        Assert.That(_work.ConfirmCalls, Is.EqualTo(1));
        Assert.That(_work.Committed, Is.True);
    }

    [Test]
    public async Task Confirm_WithNextTotpPeriod_EnablesMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(current.UtcDateTime.AddSeconds(30));

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-next-period", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        Assert.That(_work.ConfirmCalls, Is.EqualTo(1));
        Assert.That(_work.Committed, Is.True);
    }

    [Test]
    public async Task Confirm_WithCodeOutsideTotpWindow_DoesNotEnableMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(current.UtcDateTime.AddSeconds(-60));

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-outside-window", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(_work.ConfirmCalls, Is.Zero);
    }

    [Test]
    public async Task Confirm_WithExpiredEnrollment_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials(_protector.Protect(TotpHelpers.GenerateSecret()), _now.AddMinutes(-1));

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-6", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(_work.ConfirmCalls, Is.Zero);
    }

    [Test]
    public async Task Confirm_WithInvalidProtectedSecret_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials("not-a-valid-protected-value");

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-7", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(_work.ConfirmCalls, Is.Zero);
    }

    [Test]
    public async Task Verify_WithCurrentCode_PromotesPendingSession()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);
        var result = await _service.VerifyAsync(Token, code, "corr-verify", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        Assert.That(_work.ConsumedStep, Is.EqualTo(TotpHelpers.Verify(code, secret).timeStepMatched));
        Assert.That(_work.PromotedSession, Is.EqualTo(_sessionId));
        Assert.That(_work.Committed, Is.True);
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

        var result = await _service.VerifyAsync(Token, code, "corr-older", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_WhenParallelRequestConsumedTheStepFirst_IsRejectedAndSessionStaysPending()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _work.ConsumeResult = false;
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.VerifyAsync(Token, code, "corr-race", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(_work.PromotedSession, Is.Null);
        Assert.That(_work.Committed, Is.False);
        Assert.That(_work.Disposed, Is.True);
    }

    [Test]
    public async Task Verify_WhenSessionWasRevokedMeanwhile_RollsBackTheConsumedStep()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _work.PromoteResult = false;
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.VerifyAsync(Token, code, "corr-revoked", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(_work.Committed, Is.False);
        Assert.That(_work.Disposed, Is.True);
    }

    [Test]
    public async Task Confirm_WhenParallelConfirmationWonFirst_IsRejected()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        _work.ConfirmResult = false;
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-confirm-race", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        Assert.That(_work.Committed, Is.False);
    }

    [Test]
    public async Task Verify_WithWrongCode_RecordsFailureWithoutPromotingSession()
    {
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.VerifyAsync(Token, "000000", "corr-wrong", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.RecordFailedAttemptAsync(
            UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_WithReplay_IsRejected()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);
        var matched = TotpHelpers.Verify(code, secret).timeStepMatched;
        SetupVerification(secret, current, currentStep: matched);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.VerifyAsync(Token, code, "corr-replay", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_OnThreshold_RevokesPendingSession()
    {
        SetupVerification(TotpHelpers.GenerateSecret(), _now);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _sessions.Setup(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        var result = await _service.VerifyAsync(Token, "000000", "corr-lock", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _sessions.Verify(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Verify_WhileLocked_RefusesEvenTheCorrectCode_WithoutCheckingIt()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current, lockedUntil: current.AddMinutes(10));
        _sessions.Setup(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.VerifyAsync(Token, code, "corr-locked", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _sessions.Verify(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()), Times.Once);
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
        _credentials.Verify(store => store.RecordFailedAttemptAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Verify_AfterLockoutExpired_ChecksTheCodeAgain()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current, lockedUntil: current.AddSeconds(-1));
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.VerifyAsync(Token, code, "corr-expired-lock", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        Assert.That(_work.Committed, Is.True);
    }

    // owlCoder review point 3: a failed Audit write must not give a false success.
    [Test]
    public async Task Confirm_WhenAuditIsUnavailable_DoesNotCommit_AndReturnsUnavailable()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        _audit.TransactionalResult = AuditWriteResult.Unavailable();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-audit-down", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(_work.ConfirmCalls, Is.EqualTo(1));
        Assert.That(_work.Committed, Is.False);
        Assert.That(_work.Disposed, Is.True);
    }

    [Test]
    public async Task Confirm_Success_IsAuditedInTheSameTransaction()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        await _service.ConfirmEnrollAsync(Token, code, "corr-audited", CancellationToken.None);

        var write = _audit.Writes.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnabled));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(write.Transaction, Is.SameAs(_work.Transaction));
        Assert.That(write.Context.CorrelationId, Is.EqualTo("corr-audited"));
    }

    [Test]
    public async Task Verify_WhenAuditIsUnavailable_DoesNotCommit_AndReturnsUnavailable()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        _audit.TransactionalResult = AuditWriteResult.Unavailable();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.VerifyAsync(Token, code, "corr-audit-down", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(_work.Committed, Is.False);
        Assert.That(_work.Disposed, Is.True);
    }

    [Test]
    public async Task Verify_Success_IsAuditedInTheSameTransaction()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        await _service.VerifyAsync(Token, code, "corr-verified", CancellationToken.None);

        var write = _audit.Writes.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaVerify));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(write.Transaction, Is.SameAs(_work.Transaction));
    }

    [Test]
    public async Task Verify_WrongCode_WhenAuditIsUnavailable_IsStillRefused()
    {
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        _audit.TrustedResult = AuditWriteResult.Unavailable();
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.VerifyAsync(Token, "000000", "corr-refused", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyAuditDoesNotContainSecret("invalid_code", AuditEventTypes.AuthMfaVerify);
    }

    // milanPR15 review: confirm without /mfa/enroll is a client error (401), not 503.
    [Test]
    public async Task Confirm_WithoutEnrollment_IsInvalidCode_AndAuditsNoValidEnrollment()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-no-enroll", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyAuditDoesNotContainSecret("no_valid_enrollment");
    }

    [Test]
    public async Task Enroll_WhenTheDatabaseFails_ReturnsUnavailable()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _credentials.Setup(store => store.CreatePendingAsync(
                UserId, It.IsAny<string>(), 1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("storage failed"));

        var result = await _service.EnrollAsync(Token, "corr-db-down", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_audit.Writes, Is.Empty);
    }

    private readonly Guid _sessionId = Guid.NewGuid();

    private void SetupSession(AuthLevel authLevel = AuthLevel.Active, DateTimeOffset? now = null)
    {
        var sessionNow = now ?? _now;
        var session = new Session(
            _sessionId, UserId, new byte[32], Guid.NewGuid(),
            authLevel, sessionNow.AddHours(-1), sessionNow.AddHours(8));
        _sessions.Setup(store => store.FindByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private void SetupPendingCredentials(string ciphertext, DateTimeOffset? expiresAt = null)
    {
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                PendingTotpSecretCiphertext = ciphertext,
                PendingTotpSecretKeyVersion = 1,
                PendingEnrollmentExpiresAt = expiresAt ?? _now.AddMinutes(5)
            });
    }

    private void SetupVerification(
        string secret, DateTimeOffset now, long? currentStep = null, DateTimeOffset? lockedUntil = null)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(AuthLevel.MfaPending, now);
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                MfaEnabled = true,
                TotpSecretCiphertext = _protector.Protect(secret),
                TotpSecretKeyVersion = 1,
                LastTotpTimeStep = currentStep,
                MfaLockedUntil = lockedUntil
            });
    }

    private void SetupConfirmation(string secret, DateTimeOffset now)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(now: now);
        SetupPendingCredentials(_protector.Protect(secret), now.AddMinutes(5));
    }

    private sealed class MarkerTransaction : DbTransaction
    {
        public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => null;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class FakeUnitOfWork : IMfaUnitOfWork
    {
        public bool ConfirmResult { get; set; } = true;
        public bool ConsumeResult { get; set; } = true;
        public bool PromoteResult { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public long? ConsumedStep { get; private set; }
        public Guid? PromotedSession { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }

        public DbTransaction Transaction { get; } = new MarkerTransaction();

        public Task<bool> TryConfirmEnrollmentAsync(
            Guid userId, string pendingCiphertext, long timeStep, DateTimeOffset now, CancellationToken ct)
        {
            ConfirmCalls++;
            return Task.FromResult(ConfirmResult);
        }

        public Task<bool> TryConsumeTimeStepAsync(Guid userId, long timeStep, DateTimeOffset now, CancellationToken ct)
        {
            if (ConsumeResult)
                ConsumedStep = timeStep;
            return Task.FromResult(ConsumeResult);
        }

        public Task<bool> TryPromoteSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken ct)
        {
            if (PromoteResult)
                PromotedSession = sessionId;
            return Task.FromResult(PromoteResult);
        }

        public Task CommitAsync(CancellationToken ct)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private void VerifyAuditDoesNotContainSecret(string expectedReason, string eventType = AuditEventTypes.AuthMfaEnabled)
    {
        var write = _audit.Writes.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(eventType));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(write.Draft.Details, Is.EqualTo(new Dictionary<string, string> { ["reasonCode"] = expectedReason }));
        var serialized = string.Join('|', write.Draft.EventType, write.Draft.Resource?.Id, write.Context.CorrelationId,
            string.Join(',', write.Draft.Details!.Select(pair => $"{pair.Key}={pair.Value}")));
        Assert.That(serialized, Does.Not.Contain("ciphertext").And.Not.Contain("123456").And.Not.Contain("000000"));
    }

    private sealed record AuditWrite(AuditEventDraft Draft, AuditContext Context, DbTransaction? Transaction);

    // Stands in for both Audit writers behind IdentityAuditProducer.
    private sealed class RecordingAudit : ITrustedAuditWriter, ITransactionalAuditWriter
    {
        public List<AuditWrite> Writes { get; } = [];
        public AuditWriteResult TrustedResult { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());
        public AuditWriteResult TransactionalResult { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
        {
            Writes.Add(new AuditWrite(draft, context, null));
            return ValueTask.FromResult(TrustedResult);
        }

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft, AuditContext context, DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new AuditWrite(draft, context, transaction));
            return ValueTask.FromResult(TransactionalResult);
        }
    }
}
