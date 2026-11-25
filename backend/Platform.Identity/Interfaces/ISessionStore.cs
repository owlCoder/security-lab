using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Interfaces;

public interface ISessionStore
{
    Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct);

    /// <summary>
    /// Creates a session only while the account is <see cref="Enums.AccountStatus.Active"/> (IDN-03, SEC-03).
    /// The account row is locked with FOR SHARE until the session is committed, so a concurrent status change
    /// (FOR UPDATE) either waits and then revokes this session, or has already committed and this call
    /// returns null. Returns null when the account does not exist or is not active; no session is stored.
    /// </summary>
    Task<(string RawToken, Session Session)?> CreateForActiveAccountAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct);

    Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct);

    Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Revokes the session with this token. Returns the session only when this call revoked it;
    /// null when the token is unknown, the session has expired or it was already revoked,
    /// also by a concurrent call.
    /// </summary>
    Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct);
}
