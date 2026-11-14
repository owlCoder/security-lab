using Microsoft.AspNetCore.DataProtection;
using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;

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
    private Mock<IAuditWriter> _audit = null!;
    private Mock<TimeProvider> _clock = null!;
    private IDataProtector _protector = null!;
    private MfaService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        _credentials = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        _audit = new Mock<IAuditWriter>(MockBehavior.Strict);
        _clock = new Mock<TimeProvider>(MockBehavior.Strict);
        _clock.Setup(clock => clock.GetUtcNow()).Returns(_now);
        _audit.Setup(writer => writer.Write(It.IsAny<AuditEvent>()));
        _protector = new EphemeralDataProtectionProvider()
            .CreateProtector("SecurityLab", "Identity", "MfaTotpSecret", "v1");
        _service = new MfaService(_credentials.Object, _sessions.Object, _audit.Object, _clock.Object, _protector);
    }

    [Test]
    public async Task Enroll_WithoutToken_IsUnauthorized_AndWritesNoAudit()
    {
        var result = await _service.EnrollAsync(null, "corr-1", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        _audit.Verify(writer => writer.Write(It.IsAny<AuditEvent>()), Times.Never);
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
        VerifyAuditDoesNotContainSecret("already_enabled");
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
        _credentials.Verify(store => store.ConfirmAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _credentials.Setup(store => store.ConfirmAsync(UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-confirm", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        _credentials.Verify(store => store.ConfirmAsync(
            UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Confirm_WithPreviousTotpPeriod_EnablesMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(current.UtcDateTime.AddSeconds(-30));
        _credentials.Setup(store => store.ConfirmAsync(UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-previous-period", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        _credentials.Verify(store => store.ConfirmAsync(
            UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Confirm_WithNextTotpPeriod_EnablesMfa()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(current.UtcDateTime.AddSeconds(30));
        _credentials.Setup(store => store.ConfirmAsync(UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-next-period", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Success));
        _credentials.Verify(store => store.ConfirmAsync(
            UserId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
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
        _credentials.Verify(store => store.ConfirmAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Confirm_WithExpiredEnrollment_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials(_protector.Protect(TotpHelpers.GenerateSecret()), _now.AddMinutes(-1));

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-6", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        _credentials.Verify(store => store.ConfirmAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Confirm_WithInvalidProtectedSecret_DoesNotEnableMfa()
    {
        SetupSession();
        SetupPendingCredentials("not-a-valid-protected-value");

        var result = await _service.ConfirmEnrollAsync(Token, "123456", "corr-7", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        _credentials.Verify(store => store.ConfirmAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupSession(AuthLevel authLevel = AuthLevel.Active, DateTimeOffset? now = null)
    {
        var sessionNow = now ?? _now;
        var session = new Session(
            Guid.NewGuid(), UserId, new byte[32], Guid.NewGuid(),
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

    private void SetupConfirmation(string secret, DateTimeOffset now)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(now: now);
        SetupPendingCredentials(_protector.Protect(secret), now.AddMinutes(5));
    }

    private void VerifyAuditDoesNotContainSecret(string expectedDetail)
    {
        _audit.Verify(writer => writer.Write(It.Is<AuditEvent>(audit =>
            audit.Detail == expectedDetail
            && !audit.Action.Contains("ciphertext", StringComparison.Ordinal)
            && !audit.Target.Contains("ciphertext", StringComparison.Ordinal)
            && !audit.Outcome.Contains("ciphertext", StringComparison.Ordinal)
            && !audit.CorrelationId.Contains("ciphertext", StringComparison.Ordinal)
            && !audit.Detail!.Contains("ciphertext", StringComparison.Ordinal)
            && !audit.Detail!.Contains("123456", StringComparison.Ordinal)
            && !audit.Detail.Contains("000000", StringComparison.Ordinal))), Times.Once);
    }
}
