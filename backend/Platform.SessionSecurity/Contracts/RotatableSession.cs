using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: one session in a rotation chain. Only the SHA-256 hash of the session token is stored,
/// never the token itself.
/// </summary>
/// <param name="ChainId">
/// Shared by every session created from the same original session through rotation, so the
/// whole chain can be revoked at once (IDN-02, IDN-03). Not a secret, but never written to
/// the audit record or sent to the client.
/// </param>
/// <param name="SubjectId">Owner of the session; null for a pre-authentication session.</param>
/// <param name="TokenHash">Hex-encoded SHA-256 hash of the session token.</param>
/// <param name="PrivilegeLevel">Privilege level of this session.</param>
/// <param name="IssuedAt">When this session in the chain was issued.</param>
/// <param name="AbsoluteExpiresAt">
/// Absolute end of the chain. A rotation copies it unchanged, so a rotation never
/// extends the lifetime of a session.
/// </param>
/// <param name="RevokedAt">When the session was revoked; null while it is not revoked.</param>
/// <param name="IsPendingActivation">
/// True for a replacement that a rotation stored but did not activate yet. A pending session is
/// never accepted: it becomes usable only after the rotation was audited (AUD-10, fail-closed).
/// </param>
public sealed record RotatableSession(
    Guid ChainId,
    string? SubjectId,
    string TokenHash,
    SessionPrivilegeLevel PrivilegeLevel,
    DateTimeOffset IssuedAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt = null,
    bool IsPendingActivation = false)
{
    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpiredAt(DateTimeOffset now) => now >= AbsoluteExpiresAt;

    /// <summary>
    /// The only check a session validation needs: not revoked, not pending activation and not
    /// past its absolute expiry.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        !IsRevoked && !IsPendingActivation && !IsExpiredAt(now);

    // AUD-03: the default record ToString would print the token hash and the chain ID.
    public override string ToString() =>
        $"RotatableSession {{ PrivilegeLevel = {PrivilegeLevel}, IssuedAt = {IssuedAt:O}, " +
        $"AbsoluteExpiresAt = {AbsoluteExpiresAt:O}, IsRevoked = {IsRevoked}, " +
        $"IsPendingActivation = {IsPendingActivation} }}";
}
