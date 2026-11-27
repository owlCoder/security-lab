using Moq;
using OtpNet;
using Platform.Api.Enums.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Helpers;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 US-17/US-18 audit (AUD-01): a failed Audit write never gives a false success, and a refusal stays a refusal.</summary>
[TestFixture]
public sealed class MfaServiceAuditTests : MfaServiceTestBase
{
    [Test]
    public async Task Confirm_WhenAuditIsUnavailable_DoesNotCommit_AndReturnsUnavailable()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        SetupAuditResults(transactional: AuditWriteResult.Unavailable());
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var result = await _service.ConfirmEnrollAsync(Token, code, "corr-audit-down", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        VerifyConfirm(Times.Once());
        VerifyCommitted(false);
        VerifyDisposed();
    }

    [Test]
    public async Task Confirm_Success_IsAuditedInTheSameTransaction()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupConfirmation(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        await _service.ConfirmEnrollAsync(Token, code, "corr-audited", CancellationToken.None);

        var write = _auditWrites.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnabled));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(write.Transaction, Is.SameAs(_transaction));
        Assert.That(write.Context.CorrelationId, Is.EqualTo("corr-audited"));
    }

    [Test]
    public async Task Verify_WhenAuditIsUnavailable_DoesNotCommit_AndReturnsUnavailable()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        SetupAuditResults(transactional: AuditWriteResult.Unavailable());
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        var (result, _) = await _service.VerifyAsync(Token, code, "corr-audit-down", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.Unavailable));
        VerifyCommitted(false);
        VerifyDisposed();
    }

    [Test]
    public async Task Verify_Success_IsAuditedInTheSameTransaction()
    {
        var current = DateTimeOffset.UtcNow;
        var secret = TotpHelpers.GenerateSecret();
        SetupVerification(secret, current);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(current.UtcDateTime);

        await _service.VerifyAsync(Token, code, "corr-verified", CancellationToken.None);

        var write = _auditWrites.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaVerified));
        Assert.That(write.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(write.Transaction, Is.SameAs(_transaction));
    }

    [Test]
    public async Task Verify_WrongCode_WhenAuditIsUnavailable_IsStillRefused()
    {
        var current = DateTimeOffset.UtcNow;
        SetupVerification(TotpHelpers.GenerateSecret(), current);
        SetupAuditResults(trusted: AuditWriteResult.Unavailable());
        _credentials.Setup(store => store.RecordFailedAttemptAsync(
                UserId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var (result, _) = await _service.VerifyAsync(Token, "000000", "corr-refused", CancellationToken.None);

        Assert.That(result, Is.EqualTo(MfaOutcome.InvalidCode));
        VerifyAuditDoesNotContainSecret("invalid_code", AuditEventTypes.AuthMfaVerified);
    }
}
