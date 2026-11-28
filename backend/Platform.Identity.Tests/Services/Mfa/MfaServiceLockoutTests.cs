using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Helpers;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 the MFA lockout in VerifyAsync (Identity:Mfa:Lockout).</summary>
[TestFixture]
[Category("US-18")]
public sealed class MfaServiceLockoutTests : MfaServiceTestBase
{
    [Test]
    public async Task Verify_WrongCode_CountsWithTheMfaLockoutSettings()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        var service = CreateService(new MfaLockoutOptions { MaxFailedAttempts = 3, LockoutMinutes = 30 });
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 3, current, current.AddMinutes(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (result, _) = await service.VerifyAsync(Token, "000000", "corr-mfa-lockout", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.RecordFailedAttemptAsync(
            UserId, 3, current, current.AddMinutes(30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Verify_OnThreshold_RevokesPendingSession_AndAuditsTheAttemptLimit()
    {
        // Arrange
        SetupVerification(TotpHelpers.GenerateSecret(), _now);
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _sessions.Setup(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        // Act
        var (result, _) = await _service.VerifyAsync(Token, "000000", "corr-lock", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _sessions.Verify(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()), Times.Once);
        VerifyAuditDoesNotContainSecret("attempt_limit", AuditEventTypes.AuthMfaVerified, AuditOutcome.Denied);
    }

    [Test]
    public async Task Verify_WhileLocked_RefusesEvenTheCorrectCode_WithoutCheckingIt()
    {
        // Arrange
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current, lockedUntil: current.AddMinutes(10));
        _sessions.Setup(store => store.RevokeByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        // Act
        var (result, _) = await _service.VerifyAsync(Token, code, "corr-locked", CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyAuditDoesNotContainSecret("attempt_limit", AuditEventTypes.AuthMfaVerified, AuditOutcome.Denied);
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

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-expired-lock", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        VerifyCommitted(true);
    }
}
