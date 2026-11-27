using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 confirmation of the enrollment (ConfirmEnrollAsync): TOTP window, expiry and parallel confirmations.</summary>
[TestFixture]
[Category("US-17")]
public sealed class MfaServiceConfirmTests : MfaServiceTestBase
{
    [Test]
    public async Task Confirm_WithWrongCode_DoesNotEnableMfa()
    {
        SetupSession();
        var secret = TotpHelpers.GenerateSecret();
        SetupPendingCredentials(_protector.Protect(secret));

        var result = await _service.ConfirmEnrollAsync(Token, "000000", "corr-5", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyConfirm(Times.Never());
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
        VerifyConfirm(Times.Once());
        VerifyCommitted(true);
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
        VerifyConfirm(Times.Once());
        VerifyCommitted(true);
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
        VerifyConfirm(Times.Once());
        VerifyCommitted(true);
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
        VerifyConfirm(Times.Never());
    }

    [Test]
    public async Task Confirm_WithExpiredEnrollment_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials(_protector.Protect(TotpHelpers.GenerateSecret()), _now.AddMinutes(-1));

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-6", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyConfirm(Times.Never());
    }

    [Test]
    public async Task Confirm_WithInvalidProtectedSecret_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials("not-a-valid-protected-value");

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-7", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        VerifyConfirm(Times.Never());
    }

    [Test]
    public async Task Confirm_WhenParallelConfirmationWonFirst_IsRejected()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        _work.Setup(work => work.TryConfirmEnrollmentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-confirm-race", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyCommitted(false);
    }

    [Test]
    public async Task Confirm_WhenSessionOrAccountIsNoLongerActiveAtTheWrite_IsUnauthorized_AndDoesNotEnableMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        _work.Setup(work => work.TryLockActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AuthLevel>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-confirm-blocked", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyLockedSession(AuthLevel.Active);
        VerifyConfirm(Times.Never());
        VerifyCommitted(false);
        VerifyAuditDoesNotContainSecret("session_inactive");
    }

    // US-17: confirm without /mfa/enroll is a client error (401), not 503.
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
}
