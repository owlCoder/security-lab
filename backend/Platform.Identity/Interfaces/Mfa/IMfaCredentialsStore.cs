using System.Data.Common;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Interfaces.Mfa;

public interface IMfaCredentialsStore
{
    Task<MfaCredentials?> GetAsync(Guid userId, CancellationToken ct);
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// US-18: counts one wrong second factor in a single UPDATE, so parallel wrong codes cannot skip the threshold.
    /// A lockout that has expired at <paramref name="now"/> starts the counter again at 1. When the counter reaches
    /// <paramref name="maxAttempts"/>, <c>mfa_locked_until</c> is set to <paramref name="lockedUntil"/>.
    /// An account that is still locked is not counted. Returns true when the account is locked after this call
    /// (also when nothing was counted because it was already locked).
    /// </summary>
    Task<bool> RecordFailedAttemptAsync(
        Guid userId,
        int maxAttempts,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        CancellationToken ct);

    /// <summary>
    /// Opens one transaction for a successful MFA change and its Audit record. Nothing is stored unless
    /// <see cref="IMfaUnitOfWork.CommitAsync"/> is called.
    /// </summary>
    Task<IMfaUnitOfWork> BeginAsync(CancellationToken ct);
}

/// <summary>
/// The MFA credentials and the sessions share one <c>PlatformIdentityDbContext</c>, so every operation here runs in
/// <see cref="Transaction"/>. Each operation is one conditional UPDATE: PostgreSQL locks the row, and a parallel
/// request that waited for it checks the condition again after the commit and changes nothing.
/// Disposing without a commit rolls everything back.
/// </summary>
public interface IMfaUnitOfWork : IAsyncDisposable
{
    /// <summary>The open database transaction, so that the Audit record can be written in it.</summary>
    DbTransaction Transaction { get; }

    /// <summary>
    /// IDN-03 and IDN-07: locks the account (<c>FOR SHARE</c>) and then the session (<c>FOR NO KEY UPDATE</c>) and
    /// returns true only while the account is <c>Active</c> and the session belongs to it, has
    /// <paramref name="level"/>, and is neither revoked nor expired at <paramref name="now"/>. Call it first in the
    /// transaction: a status change or revocation that is already running makes this wait and then read the new
    /// state; one that starts later waits for the commit. Always account before session, the same order as
    /// <c>ChangeAccountStatus</c>, so the two cannot deadlock.
    /// </summary>
    Task<bool> TryLockActiveSessionAsync(
        Guid userId,
        Guid sessionId,
        AuthLevel level,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// US-17: stores a pending enrollment in one statement (insert, or update of an expired one). Returns false when
    /// MFA is already enabled or an unexpired enrollment is pending, also when a parallel request stored one first.
    /// </summary>
    Task<bool> TryCreatePendingAsync(
        Guid userId,
        string encryptedSecret,
        int keyVersion,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// US-17: turns the pending enrollment with <paramref name="pendingCiphertext"/> into the active secret.
    /// Only while MFA is not enabled and the enrollment has not expired at <paramref name="now"/>, so of two parallel
    /// confirmations only one succeeds. The confirming time step is stored as consumed.
    /// </summary>
    Task<bool> TryConfirmEnrollmentAsync(
        Guid userId,
        string pendingCiphertext,
        long timeStep,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// US-18: stores <paramref name="timeStep"/> as the last used step only when it is newer than the stored one and
    /// the account is not locked at <paramref name="now"/>. Clears the failed attempts and the lockout.
    /// Returns false for a replayed or older step, also when a parallel request consumed it first.
    /// </summary>
    Task<bool> TryConsumeTimeStepAsync(Guid userId, long timeStep, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// US-18 and IDN-11: replaces the <c>MfaPending</c> session with a new <c>Active</c> session. The pending session
    /// is revoked in one conditional UPDATE (still <c>MfaPending</c>, neither revoked nor expired at
    /// <paramref name="now"/>), and a new session of <paramref name="userId"/> with a new token and handle is stored,
    /// expiring after <paramref name="lifetime"/>. The token from before the second factor is rejected afterwards.
    /// Returns null, and stores nothing, when the pending session was revoked or expired in the meantime.
    /// </summary>
    Task<(string RawToken, Session Session)?> TryReplaceWithActiveSessionAsync(
        Guid userId,
        Guid pendingSessionId,
        TimeSpan lifetime,
        DateTimeOffset now,
        CancellationToken ct);

    Task CommitAsync(CancellationToken ct);
}
