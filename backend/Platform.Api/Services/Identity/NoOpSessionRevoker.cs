using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// Stub dok IDN-02 ne napravi pravi opoziv sesija.
public sealed class NoOpSessionRevoker : ISessionManager
{
    public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => 0;
    public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => false;
    public bool IsSessionActive(Guid userId, Guid sessionHandle, CancellationToken ct = default) => false;
    public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default) => Array.Empty<SessionInfo>();
}
