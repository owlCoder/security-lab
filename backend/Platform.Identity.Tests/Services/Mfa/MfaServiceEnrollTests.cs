using System.Data.Common;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Models;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 enrollment (EnrollAsync): who may start it and what is stored.</summary>
[TestFixture]
[Category("US-17")]
public sealed class MfaServiceEnrollTests : MfaServiceTestBase
{
    [Test]
    public async Task Enroll_WithoutToken_IsUnauthorized_AndWritesNoAudit()
    {
        var result = await _service.EnrollAsync(null, Password, "corr-1", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_auditWrites, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithMfaPendingSession_IsUnauthorized()
    {
        SetupSession(AuthLevel.MfaPending);

        var result = await _service.EnrollAsync(Token, Password, "corr-2", CancellationToken.None);

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

        var result = await _service.EnrollAsync(Token, Password, "corr-3", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.AlreadyEnabled));
        Assert.That(result.Uri, Is.Null);
        VerifyCreatePending(Times.Never());
        Assert.That(_auditWrites, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithActiveSession_CreatesPendingEnrollment_AndReturnsTotpUri()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");

        var result = await _service.EnrollAsync(Token, Password, "corr-success", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Success));
        Assert.That(result.Uri, Does.StartWith("otpauth://totp/"));
        Assert.That(result.Uri, Does.Contain("student%40example.test"));
        VerifyLockedSession(AuthLevel.Active);
        _work.Verify(work => work.TryCreatePendingAsync(
            UserId, It.Is<string>(ciphertext => ciphertext.Length > 0), 1, _now.AddMinutes(10), _now,
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyCommitted(true);
        var started = _auditWrites.Single();
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
        _work.Setup(work => work.TryCreatePendingAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.EnrollAsync(Token, Password, "corr-4", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.EnrollmentPending));
        Assert.That(result.Uri, Is.Null);
        VerifyCreatePending(Times.Once());
        VerifyCommitted(false);
    }

    // IDN-03 and IDN-07: the session and the account are checked again under a lock in the writing transaction.
    [Test]
    public async Task Enroll_WhenSessionOrAccountIsNoLongerActiveAtTheWrite_IsUnauthorized_AndStoresNothing()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _work.Setup(work => work.TryLockActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AuthLevel>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.EnrollAsync(Token, Password, "corr-enroll-blocked", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        VerifyLockedSession(AuthLevel.Active);
        VerifyCreatePending(Times.Never());
        VerifyCommitted(false);
        Assert.That(_auditWrites, Is.Empty);
    }

    [Test]
    public async Task Enroll_WhenTheDatabaseFails_ReturnsUnavailable()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _work.Setup(work => work.TryCreatePendingAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("storage failed"));

        var result = await _service.EnrollAsync(Token, Password, "corr-db-down", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_auditWrites, Is.Empty);
    }

    // IDN-06 (#252) step-up: the current password of the session's account is checked before enrollment.

    [Test]
    public async Task Enroll_ChecksThePasswordAgainstTheSessionAccountsEmail()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);

        var result = await _service.EnrollAsync(Token, Password, "corr-step-up", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Success));
        _verifier.Verify(verifier => verifier.VerifyAsync(Email, Password, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Enroll_WithWrongPassword_IsUnauthorized_StoresNothing_AndAuditsInvalidPassword()
    {
        SetupSession();
        SetupPasswordCheck(CredentialResult.Invalid);

        var result = await _service.EnrollAsync(Token, "wrong-password", "corr-wrong-password", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        // Nothing about MFA is read or written before the password is accepted.
        _credentials.Verify(store => store.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _credentials.Verify(store => store.BeginAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyCreatePending(Times.Never());
        VerifyAuditDoesNotContainSecret("invalid_password", AuditEventTypes.AuthMfaEnrollmentStarted);
        Assert.That(_auditWrites.Single().Context.Actor, Is.EqualTo(AuditActor.ForUser(UserId)));
        Assert.That(string.Join('|', _auditWrites.Single().Draft.Details!.Values), Does.Not.Contain("wrong-password"));
    }

    [Test]
    public async Task Enroll_WhenTheAccountIsLocked_IsUnauthorized_AndAuditsAccountLocked()
    {
        SetupSession();
        SetupPasswordCheck(CredentialResult.LockedOut(UserId));

        var result = await _service.EnrollAsync(Token, Password, "corr-locked", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyCreatePending(Times.Never());
        VerifyAuditDoesNotContainSecret("account_locked", AuditEventTypes.AuthMfaEnrollmentStarted);
    }

    [Test]
    public async Task Enroll_WithWrongPassword_WhenMfaIsAlreadyEnabled_IsUnauthorized_NotConflict()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId) { MfaEnabled = true });
        SetupPasswordCheck(CredentialResult.Invalid);

        var result = await _service.EnrollAsync(Token, "wrong-password", "corr-wrong-enabled", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
    }

    [Test]
    public async Task Enroll_WhenTheVerifierAcceptsAnotherAccount_IsUnauthorized()
    {
        SetupSession();
        SetupPasswordCheck(new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active));

        var result = await _service.EnrollAsync(Token, Password, "corr-other-account", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyCreatePending(Times.Never());
        VerifyAuditDoesNotContainSecret("invalid_password", AuditEventTypes.AuthMfaEnrollmentStarted);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task Enroll_WithoutPassword_IsInvalidRequest_AndChargesNoAttempt(string? password)
    {
        SetupSession();

        var result = await _service.EnrollAsync(Token, password, "corr-no-password", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.InvalidRequest));
        VerifyPasswordChecked(Times.Never());
        Assert.That(_auditWrites, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithTooLongPassword_IsInvalidRequest_AndChargesNoAttempt()
    {
        SetupSession();
        var password = new string('x', Platform.Identity.Services.CredentialVerifier.MaxPasswordLength + 1);

        var result = await _service.EnrollAsync(Token, password, "corr-long-password", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.InvalidRequest));
        VerifyPasswordChecked(Times.Never());
    }

    [Test]
    public async Task Enroll_WhenThePasswordCheckDatabaseFails_ReturnsUnavailable_AndStoresNothing()
    {
        SetupSession();
        _verifier.Setup(verifier => verifier.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("storage failed"));

        var result = await _service.EnrollAsync(Token, Password, "corr-verifier-down", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unavailable));
        VerifyCreatePending(Times.Never());
        Assert.That(_auditWrites, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithoutSession_DoesNotCheckThePassword()
    {
        var result = await _service.EnrollAsync(null, Password, "corr-no-session", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        VerifyPasswordChecked(Times.Never());
    }
}
