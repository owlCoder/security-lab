using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// IDN-06: TOTP enrollment (US-17) and the second factor of a login (US-18).
/// A successful change (MFA enabled, or the pending session replaced by a new active one) is one transaction with its Audit record: if the record is
/// not stored, nothing is committed and the answer is <see cref="MfaOutcome.Unavailable"/>.
/// A refusal stays a refusal when its Audit record fails (CONTRACTS.md, producer obligation 2).
/// </summary>
public sealed class MfaService(
    IMfaCredentialsStore mfaCredentialsStore,
    ISessionStore sessionStore,
    IdentityAuditProducer audit,
    TimeProvider clock,
    ITotpSecretProtector secretProtector,
    IOptions<MfaLockoutOptions> lockout,
    ILogger<MfaService> logger) : IMfaService
{
    private const int SecretKeyVersion = 1;
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(10);

    private readonly IMfaCredentialsStore _mfaCredentialsStore = mfaCredentialsStore;
    private readonly ISessionStore _sessionStore = sessionStore;
    private readonly IdentityAuditProducer _audit = audit;
    private readonly TimeProvider _clock = clock;
    private readonly ITotpSecretProtector _secretProtector = secretProtector;
    private readonly MfaLockoutOptions _lockout = lockout.Value;
    private readonly ILogger<MfaService> _logger = logger;

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
            return (MfaOutcome.AlreadyEnabled, null);

        var email = await _mfaCredentialsStore.GetEmailAsync(session.UserId, ct);
        if (email is null)
            return (MfaOutcome.Unavailable, null);

        var secret = TotpHelpers.GenerateSecret();
        var encryptedSecret = _secretProtector.Protect(secret);
        try
        {
            // The session and the account were read without a lock. The pending enrollment is stored only while
            // both are still valid, in the same transaction, so a block or revocation in between stores nothing.
            await using var work = await _mfaCredentialsStore.BeginAsync(ct);
            var now = _clock.GetUtcNow();
            if (!await work.TryLockActiveSessionAsync(session.UserId, session.Id, AuthLevel.Active, now, ct))
                return (MfaOutcome.Unauthorized, null);

            if (!await work.TryCreatePendingAsync(
                    session.UserId, encryptedSecret, SecretKeyVersion, now.Add(EnrollmentLifetime), now, ct))
                return (MfaOutcome.EnrollmentPending, null);

            await work.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            _logger.LogError(ex, "MFA enrollment could not be stored: the database failed.");
            return (MfaOutcome.Unavailable, null);
        }

        // A pending enrollment enables nothing; MFA is enabled only by the confirmation, which is atomic with its
        // record. The pending row is already stored, so the record must not be lost to a cancelled request.
        await _audit.MfaEnrollmentStartedAsync(session.UserId, correlationId, CancellationToken.None);
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

        var userId = session.UserId;
        // No row yet means confirm without /mfa/enroll: a client error like every other missing enrollment.
        var credentials = await _mfaCredentialsStore.GetAsync(userId, ct);
        if (credentials is null)
            return await RefuseEnableAsync(userId, "no_valid_enrollment", correlationId, MfaOutcome.InvalidCode, ct);
        if (credentials.MfaEnabled)
            return await RefuseEnableAsync(userId, "already_enabled", correlationId, MfaOutcome.AlreadyEnabled, ct);

        if (credentials.PendingTotpSecretCiphertext is not { } pendingCiphertext
            || credentials.PendingTotpSecretKeyVersion != SecretKeyVersion
            || credentials.PendingEnrollmentExpiresAt is null
            || credentials.PendingEnrollmentExpiresAt <= _clock.GetUtcNow())
            return await RefuseEnableAsync(userId, "no_valid_enrollment", correlationId, MfaOutcome.InvalidCode, ct);

        string secret;
        try
        {
            secret = _secretProtector.Unprotect(pendingCiphertext);
        }
        catch (CryptographicException)
        {
            return await RefuseEnableAsync(userId, "invalid_enrollment", correlationId, MfaOutcome.Unavailable, ct);
        }

        var result = TotpHelpers.Verify(code, secret);
        if (!result.success || IsReplayed(credentials, result.timeStepMatched))
            return await RefuseEnableAsync(userId, "invalid_code", correlationId, MfaOutcome.InvalidCode, ct);

        var sessionInactive = false;

        // The checks above read the rows without a lock. In the transaction the session and the account are
        // locked and checked again (a block or revocation in between enables nothing), and the conditional UPDATE
        // decides: of two parallel confirmations only one finds MFA still disabled and the same pending enrollment.
        try
        {
            await using var work = await _mfaCredentialsStore.BeginAsync(ct);
            var now = _clock.GetUtcNow();
            if (!await work.TryLockActiveSessionAsync(userId, session.Id, AuthLevel.Active, now, ct))
                sessionInactive = true;
            else if (await work.TryConfirmEnrollmentAsync(userId, pendingCiphertext, result.timeStepMatched, now, ct))
            {
                var recorded = await _audit.MfaEnabledAsync(userId, correlationId, work.Transaction, ct);

                // No Audit record, no MFA: leaving the scope without a commit rolls the change back.
                if (!recorded.IsRecorded)
                {
                    _logger.LogError("MFA enrollment was rolled back: Audit write ended as {Status}.", recorded.Status);
                    return MfaOutcome.Unavailable;
                }

                await work.CommitAsync(ct);
                return MfaOutcome.Success;
            }
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            _logger.LogError(ex, "MFA enrollment was rolled back: the database failed.");
            return MfaOutcome.Unavailable;
        }

        // The transaction above is closed here; it changed nothing.
        return sessionInactive
            ? await RefuseEnableAsync(userId, "session_inactive", correlationId, MfaOutcome.Unauthorized, ct)
            : await RefuseEnableAsync(userId, "no_valid_enrollment", correlationId, MfaOutcome.InvalidCode, ct);
    }

    public async Task<(MfaOutcome Outcome, IssuedSession? Session)> VerifyAsync(
        string? rawToken,
        string? code,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (MfaOutcome.InvalidRequest, null);
        if (string.IsNullOrWhiteSpace(rawToken))
            return (MfaOutcome.Unauthorized, null);

        var session = await _sessionStore.FindByTokenAsync(rawToken, ct);
        if (session is null || !session.IsActiveAt(_clock.GetUtcNow()) || session.AuthLevel != AuthLevel.MfaPending)
            return (MfaOutcome.Unauthorized, null);

        var userId = session.UserId;
        var credentials = await _mfaCredentialsStore.GetAsync(userId, ct);
        if (credentials is null || !credentials.MfaEnabled)
            return (MfaOutcome.Unauthorized, null);

        // US-18 lockout: while it lasts no code is checked, not even a correct one. The answer is the same
        // 401 invalid_mfa as for a wrong code, so it does not tell an attacker that the account is locked.
        // TryConsumeTimeStepAsync repeats this condition in its WHERE as the second line of defence.
        if (credentials.MfaLockedUntil is { } lockedUntil && lockedUntil > _clock.GetUtcNow())
        {
            await _sessionStore.RevokeByTokenAsync(rawToken, ct);
            return (await DenyVerifyByAttemptLimitAsync(userId, correlationId), null);
        }

        if (credentials.TotpSecretCiphertext is null
            || credentials.TotpSecretKeyVersion != SecretKeyVersion)
            return (MfaOutcome.Unavailable, null);

        string secret;
        try
        {
            secret = _secretProtector.Unprotect(credentials.TotpSecretCiphertext);
        }
        catch (CryptographicException)
        {
            return (await RefuseVerifyAsync(userId, "invalid_secret", correlationId, MfaOutcome.Unavailable), null);
        }

        var result = TotpHelpers.Verify(code, secret);
        var replayed = result.success && IsReplayed(credentials, result.timeStepMatched);
        if (!result.success || replayed)
        {
            var now = _clock.GetUtcNow();
            var locked = await _mfaCredentialsStore.RecordFailedAttemptAsync(
                userId,
                _lockout.MaxFailedAttempts,
                now,
                now.AddMinutes(_lockout.LockoutMinutes),
                ct);
            if (locked)
                await _sessionStore.RevokeByTokenAsync(rawToken, ct);

            if (locked)
                return (await DenyVerifyByAttemptLimitAsync(userId, correlationId), null);

            return (await RefuseVerifyAsync(
                userId, replayed ? "replayed_code" : "invalid_code", correlationId, MfaOutcome.InvalidCode), null);
        }

        // The check above is only a fast path. The step is consumed, the pending session replaced and the Audit
        // record written in one transaction, and each UPDATE repeats its condition: a parallel request with the same
        // code (also from another pending session of this user) waits for the row and then matches nothing.
        // IDN-11: the replacement is a new session with a new token, so the cookie from before the second factor
        // stops working; a rollback keeps the pending session and stores no new one.
        string refusal;
        MfaOutcome refusalOutcome;
        try
        {
            await using var work = await _mfaCredentialsStore.BeginAsync(ct);
            var now = _clock.GetUtcNow();
            if (!await work.TryLockActiveSessionAsync(userId, session.Id, AuthLevel.MfaPending, now, ct))
            {
                // Blocked, revoked or expired since it was read: nothing is consumed or replaced.
                (refusal, refusalOutcome) = ("session_inactive", MfaOutcome.Unauthorized);
            }
            else if (!await work.TryConsumeTimeStepAsync(userId, result.timeStepMatched, now, ct))
            {
                (refusal, refusalOutcome) = ("replayed_code", MfaOutcome.InvalidCode);
            }
            else if (await work.TryReplaceWithActiveSessionAsync(userId, session.Id, StoreSessionCreator.Lifetime, now, ct)
                     is not { } active)
            {
                // Revoked or expired in the meantime: leaving without a commit also gives the step back.
                (refusal, refusalOutcome) = ("session_inactive", MfaOutcome.Unauthorized);
            }
            else
            {
                var recorded = await _audit.MfaVerifiedAsync(userId, correlationId, work.Transaction, ct);

                // No Audit record, no active session: the session stays MfaPending and the step unused.
                if (!recorded.IsRecorded)
                {
                    _logger.LogError("MFA verification was rolled back: Audit write ended as {Status}.", recorded.Status);
                    return (MfaOutcome.Unavailable, null);
                }

                await work.CommitAsync(ct);
                return (MfaOutcome.Success,
                    new IssuedSession(active.RawToken, active.Session.SessionHandle, active.Session.ExpiresAt));
            }
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            _logger.LogError(ex, "MFA verification was rolled back: the database failed.");
            return (MfaOutcome.Unavailable, null);
        }

        // The transaction above is closed here; it changed nothing.
        return (await RefuseVerifyAsync(userId, refusal, correlationId, refusalOutcome), null);
    }

    // A TOTP step can be used once: the same step and every older one are refused.
    private static bool IsReplayed(MfaCredentials credentials, long timeStep) =>
        credentials.LastTotpTimeStep is { } last && timeStep <= last;

    private bool IsActiveSession(Session session) =>
        session.RevokedAt is null
        && session.AuthLevel == AuthLevel.Active
        && session.ExpiresAt > _clock.GetUtcNow();

    /// <summary>A refused confirmation changes nothing, so a failed Audit write does not change the answer.</summary>
    private async Task<MfaOutcome> RefuseEnableAsync(
        Guid userId, string reasonCode, string correlationId, MfaOutcome outcome, CancellationToken ct)
    {
        await _audit.MfaEnableFailedAsync(userId, reasonCode, correlationId, ct);
        return outcome;
    }

    /// <summary>
    /// A refused verification stays refused when its Audit write fails. It may already have counted a failed
    /// attempt or revoked the session, so the record is written with <see cref="CancellationToken.None"/>.
    /// </summary>
    private async Task<MfaOutcome> RefuseVerifyAsync(
        Guid userId, string reasonCode, string correlationId, MfaOutcome outcome)
    {
        await _audit.MfaVerifyFailedAsync(userId, reasonCode, correlationId, CancellationToken.None);
        return outcome;
    }

    /// <summary>
    /// The attempt limit: the same 401 as a wrong code for the caller, Denied with attempt_limit in the audit.
    /// The session may already be revoked, so the record is written with <see cref="CancellationToken.None"/>.
    /// </summary>
    private async Task<MfaOutcome> DenyVerifyByAttemptLimitAsync(Guid userId, string correlationId)
    {
        await _audit.MfaVerifyDeniedByAttemptLimitAsync(userId, correlationId, CancellationToken.None);
        return MfaOutcome.InvalidCode;
    }
}
