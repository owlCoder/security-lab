// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: storage of rotatable sessions. Sessions are looked up only by token hash.
/// </summary>
public interface ISessionRotationStore
{
    ValueTask<RotatableSession?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically revokes the session with <paramref name="oldTokenHash"/> and stores
    /// <paramref name="replacement"/>. When several calls use the same old hash at the same time,
    /// exactly one returns <see cref="SessionRotationStoreStatus.Rotated"/>.
    /// </summary>
    ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes every active session of the chain. Returns how many were revoked.</summary>
    ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active session of the subject, including sessions created by rotation
    /// (IDN-03, UC-05). Returns how many were revoked.
    /// </summary>
    ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
