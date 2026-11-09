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
    /// <paramref name="replacement"/> as pending activation, so it cannot be used yet. When several
    /// calls use the same old hash at the same time, exactly one returns
    /// <see cref="SessionRotationStoreStatus.Rotated"/>. The replacement becomes usable only
    /// through <see cref="ActivateAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state of the old session is checked again inside the same atomic operation, because
    /// it can change after <see cref="FindAsync"/> (a concurrent rotation, a revocation, or the
    /// absolute expiry). The checks run in this order and the first match wins:
    /// </para>
    /// <list type="number">
    /// <item><description>No session with <paramref name="oldTokenHash"/>, or only a session that
    /// is still pending activation: <see cref="SessionRotationStoreStatus.NotFound"/>.
    /// </description></item>
    /// <item><description>The old session is revoked:
    /// <see cref="SessionRotationStoreStatus.AlreadyRevoked"/>.</description></item>
    /// <item><description><paramref name="now"/> is at or after the absolute expiry of the old
    /// session: <see cref="SessionRotationStoreStatus.Expired"/>.</description></item>
    /// <item><description>Otherwise the old session is revoked at <paramref name="now"/>, the
    /// replacement is stored as pending activation, and the result is
    /// <see cref="SessionRotationStoreStatus.Rotated"/>.</description></item>
    /// </list>
    /// <para>
    /// For every result other than <see cref="SessionRotationStoreStatus.Rotated"/> nothing is
    /// changed: the replacement is not stored and the old session is not modified.
    /// </para>
    /// <para>
    /// Preconditions: <paramref name="replacement"/> has the same
    /// <see cref="RotatableSession.ChainId"/> and <see cref="RotatableSession.AbsoluteExpiresAt"/>
    /// as the old session, so a rotation never extends the session lifetime and the chain can
    /// still be revoked as a whole; it is not revoked; and
    /// <see cref="RotatableSession.IsPendingActivation"/> is true.
    /// </para>
    /// </remarks>
    ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically activates a replacement stored by <see cref="RotateAsync"/>. Returns true only
    /// when the session exists, is pending activation, is not revoked and has not reached its
    /// absolute expiry at <paramref name="now"/>. Otherwise nothing is changed and the result is
    /// false, for example when the chain was revoked by a logout in the meantime.
    /// </summary>
    ValueTask<bool> ActivateAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every session of the chain that is not revoked yet, including a replacement that
    /// is pending activation. Returns how many were revoked. Idempotent: sessions that are
    /// already revoked are not changed, so a repeated call returns 0 and is safe to retry.
    /// </summary>
    ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every session of the subject that is not revoked yet, including sessions created
    /// by rotation (IDN-03, UC-05). Returns how many were revoked. Idempotent in the same way as
    /// <see cref="RevokeChainAsync"/>.
    /// </summary>
    ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
