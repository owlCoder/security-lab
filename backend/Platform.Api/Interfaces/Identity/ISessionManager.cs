using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface ISessionManager
{
    Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default);

    Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default);

    Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// US-15: the same answer as <see cref="ValidateAsync"/>, but the call records no activity on the session.
    /// The CSRF check uses it before it refuses a request, so a refused request does not extend the session.
    /// The default is for implementations that record no activity (test doubles); one that does must override it.
    /// </summary>
    Task<ValidateSession?> PeekAsync(string token, CancellationToken ct = default) => ValidateAsync(token, ct);

    Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default);
}
