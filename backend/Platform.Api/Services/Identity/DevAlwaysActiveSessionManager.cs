using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// DEV STUB: u Developmentu svaka sesija iz stub identiteta je aktivna, da se endpointi mogu ručno probati.
/// Registruje se SAMO u Development okruzenju, do prave IDN-02 implementacije.
public sealed class DevAlwaysActiveSessionManager : ISessionManager
{
    public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => 0;
    public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => false;
    public bool IsSessionActive(Guid userId, Guid sessionHandle, CancellationToken ct = default) => true;
    public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default) => Array.Empty<SessionInfo>();
}
