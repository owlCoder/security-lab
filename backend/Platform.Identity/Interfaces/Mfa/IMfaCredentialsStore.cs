using System.Data.Common;
using Platform.Identity.Entities;

namespace Platform.Identity.Interfaces.Mfa;

public interface IMfaCredentialsStore
{
    Task<MfaCredentials?> GetAsync(Guid userId, CancellationToken ct);
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    Task<bool> CreatePendingAsync(
        Guid userId,
        string encryptedSecret,
        int keyVersion,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    Task<bool> RecordFailedAttemptAsync(
        Guid userId,
        int maxAttempts,
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
    /// US-18: raises the session from <c>MfaPending</c> to <c>Active</c> while it is neither revoked nor expired.
    /// </summary>
    Task<bool> TryPromoteSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken ct);

    Task CommitAsync(CancellationToken ct);
}
