namespace Platform.Api.Services.Identity;

public sealed record SessionInfo(
    Guid Handle,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset ExpiresAt,
    bool IsRevoked);

/// <summary>
/// Revoke and list sessions. Callers must already have checked permission.
/// See docs/contracts/ISessionManagement.md.
/// </summary>
public interface ISessionManager
{
    /// <summary>Revokes all active sessions of the user</summary>
    int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Revokes one session by public handle, only if it belongs to ownerUserId.
    /// Returns false if it does not exist or belongs to someone else (indistinguishable).
    /// </summary>
    bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default);

    /// <summary>
    /// Checks whether or not users session is Active
    /// Used for each request to determine its session validity
    /// </summary>
    bool IsSessionActive(Guid userId, Guid sessionHandle, CancellationToken ct = default);

    IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default);
}