using Platform.Identity.Domain;

namespace Platform.Identity.Services;

public interface ISessionStore
{
    Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct);

    Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct);

    /// <summary>
    /// Revokes the session with this token. Returns the session only when this call revoked it;
    /// null when the token is unknown or the session was already revoked.
    /// </summary>
    Task<Session?> RevokeByTokenAsync(string rawToken, CancellationToken ct);
}
