using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity;

namespace Platform.Api.Services.Identity;

public sealed class MfaService(
    IMfaCredentialsStore mfaCredentialsStore,
    ISessionStore sessionStore,
    IAuditWriter audit,
    TimeProvider clock,
    IDataProtector secretProtector,
    IOptions<IdentityLockoutOptions> lockout) : IMfaService
{
    private const int SecretKeyVersion = 1;
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(10);

    private readonly IMfaCredentialsStore _mfaCredentialsStore = mfaCredentialsStore;
    private readonly ISessionStore _sessionStore = sessionStore;
    private readonly IAuditWriter _audit = audit;
    private readonly TimeProvider _clock = clock;
    private readonly IDataProtector _secretProtector = secretProtector;
    private readonly IdentityLockoutOptions _lockout = lockout.Value;

    public async Task<(MfaOutcome Outcome, string? Uri)> EnrollAsync(
        string? rawToken,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return (MfaOutcome.Unauthorized, null);

        var session = await _sessionStore.FindByTokenAsync(rawToken, ct);
        if (session is null || !IsActiveSession(session))
            return (MfaOutcome.Unauthorized, null);

        var credentials = await _mfaCredentialsStore.GetAsync(session.UserId, ct);
        if (credentials?.MfaEnabled == true)
        {
            WriteAudit(session.UserId, "auth.mfa.enroll", "denied", correlationId, "already_enabled");
            return (MfaOutcome.AlreadyEnabled, null);
        }

        var email = await _mfaCredentialsStore.GetEmailAsync(session.UserId, ct);
        if (email is null)
            return (MfaOutcome.Unavailable, null);

        var secret = TotpHelpers.GenerateSecret();
        var encryptedSecret = _secretProtector.Protect(secret);
        var expiresAt = _clock.GetUtcNow().Add(EnrollmentLifetime);
        var stored = await _mfaCredentialsStore.CreatePendingAsync(
            session.UserId,
            encryptedSecret,
            SecretKeyVersion,
            expiresAt,
            ct);

        if (!stored)
        {
            WriteAudit(session.UserId, "auth.mfa.enroll", "denied", correlationId, "enrollment_pending");
            return (MfaOutcome.EnrollmentPending, null);
        }

        WriteAudit(session.UserId, "auth.mfa.enroll", "success", correlationId, null);
        return (MfaOutcome.Success, TotpHelpers.GenerateTotpUri(email, secret));
    }

    public async Task<MfaOutcome> ConfirmEnrollAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return MfaOutcome.InvalidRequest;

        if (string.IsNullOrWhiteSpace(rawToken))
            return MfaOutcome.Unauthorized;

        var session = await _sessionStore.FindByTokenAsync(rawToken, ct);
        if (session is null || !IsActiveSession(session))
            return MfaOutcome.Unauthorized;

        var credentials = await _mfaCredentialsStore.GetAsync(session.UserId, ct);
        if (credentials is null)
            return MfaOutcome.Unavailable;
        if (credentials.MfaEnabled)
            return MfaOutcome.AlreadyEnabled;

        if (credentials.PendingTotpSecretCiphertext is null
            || credentials.PendingTotpSecretKeyVersion != SecretKeyVersion
            || credentials.PendingEnrollmentExpiresAt is null
            || credentials.PendingEnrollmentExpiresAt <= _clock.GetUtcNow())
        {
            WriteAudit(session.UserId, "auth.mfa.enroll.confirm", "failure", correlationId, "no_valid_enrollment");
            return MfaOutcome.InvalidCode;
        }

        string secret;
        try
        {
            secret = _secretProtector.Unprotect(credentials.PendingTotpSecretCiphertext);
        }
        catch (CryptographicException)
        {
            WriteAudit(session.UserId, "auth.mfa.enroll.confirm", "failure", correlationId, "invalid_enrollment");
            return MfaOutcome.Unavailable;
        }

        var result = TotpHelpers.Verify(code, secret);
        if (!result.success || IsReplayed(credentials, result.timeStepMatched))
        {
            WriteAudit(session.UserId, "auth.mfa.enroll.confirm", "failure", correlationId, "invalid_code");
            return MfaOutcome.InvalidCode;
        }

        // The check above read the row without a lock. The conditional UPDATE decides: of two parallel
        // confirmations only one finds MFA still disabled and the same pending enrollment.
        await using (var work = await _mfaCredentialsStore.BeginAsync(ct))
        {
            if (!await work.TryConfirmEnrollmentAsync(
                    session.UserId,
                    credentials.PendingTotpSecretCiphertext,
                    result.timeStepMatched,
                    _clock.GetUtcNow(),
                    ct))
            {
                WriteAudit(session.UserId, "auth.mfa.enroll.confirm", "failure", correlationId, "no_valid_enrollment");
                return MfaOutcome.InvalidCode;
            }

            await work.CommitAsync(ct);
        }

        WriteAudit(session.UserId, "auth.mfa.enroll.confirm", "success", correlationId, null);
        return MfaOutcome.Success;
    }

    public async Task<MfaOutcome> VerifyAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return MfaOutcome.InvalidRequest;
        if (string.IsNullOrWhiteSpace(rawToken))
            return MfaOutcome.Unauthorized;

        var session = await _sessionStore.FindByTokenAsync(rawToken, ct);
        if (session is null || !session.IsActiveAt(_clock.GetUtcNow()) || session.AuthLevel != AuthLevel.MfaPending)
            return MfaOutcome.Unauthorized;

        var credentials = await _mfaCredentialsStore.GetAsync(session.UserId, ct);
        if (credentials is null || !credentials.MfaEnabled)
            return MfaOutcome.Unauthorized;
        if (credentials.TotpSecretCiphertext is null
            || credentials.TotpSecretKeyVersion != SecretKeyVersion)
            return MfaOutcome.Unavailable;

        string secret;
        try
        {
            secret = _secretProtector.Unprotect(credentials.TotpSecretCiphertext);
        }
        catch (CryptographicException)
        {
            WriteAudit(session.UserId, "auth.mfa.verify", "failure", correlationId, "invalid_secret");
            return MfaOutcome.Unavailable;
        }

        var result = TotpHelpers.Verify(code, secret);
        if (!result.success || IsReplayed(credentials, result.timeStepMatched))
        {
            var locked = await _mfaCredentialsStore.RecordFailedAttemptAsync(
                session.UserId,
                _lockout.MaxFailedAccessAttempts,
                _clock.GetUtcNow().AddMinutes(_lockout.LockoutMinutes),
                ct);
            if (locked)
                await _sessionStore.RevokeByTokenAsync(rawToken, ct);

            WriteAudit(session.UserId, "auth.mfa.verify", "failure", correlationId,
                locked ? "locked" : "invalid_code");
            return MfaOutcome.InvalidCode;
        }

        // The check above is only a fast path. The step is consumed and the session promoted in one transaction,
        // and each UPDATE repeats its condition: a parallel request with the same code (also from another pending
        // session of this user) waits for the row and then matches nothing.
        await using (var work = await _mfaCredentialsStore.BeginAsync(ct))
        {
            var now = _clock.GetUtcNow();
            if (!await work.TryConsumeTimeStepAsync(session.UserId, result.timeStepMatched, now, ct))
            {
                WriteAudit(session.UserId, "auth.mfa.verify", "failure", correlationId, "replayed");
                return MfaOutcome.InvalidCode;
            }

            // Revoked or expired in the meantime: leaving without a commit also gives the step back.
            if (!await work.TryPromoteSessionAsync(session.Id, now, ct))
                return MfaOutcome.Unauthorized;

            await work.CommitAsync(ct);
        }

        WriteAudit(session.UserId, "auth.mfa.verify", "success", correlationId, null);
        return MfaOutcome.Success;
    }

    // A TOTP step can be used once: the same step and every older one are refused.
    private static bool IsReplayed(MfaCredentials credentials, long timeStep) =>
        credentials.LastTotpTimeStep is { } last && timeStep <= last;

    private bool IsActiveSession(Session session) =>
        session.RevokedAt is null
        && session.AuthLevel == AuthLevel.Active
        && session.ExpiresAt > _clock.GetUtcNow();

    private void WriteAudit(
        Guid userId,
        string action,
        string outcome,
        string correlationId,
        string? detail) =>
        _audit.Write(new AuditEvent(
            _clock.GetUtcNow(),
            userId,
            action,
            userId.ToString(),
            outcome,
            correlationId,
            detail));
}