using Platform.Api.Models.Identity;
using Platform.Identity.Enums;

namespace Platform.Api.Interfaces.Identity;

/// <summary>
/// What login needs from the session store (US-03). ISessionManager has no "create", so this is the
/// proposed contract. Every call must produce a NEW token (session fixation protection).
/// Return null when the store cannot create a session; login then answers 503.
/// Throw <see cref="AccountNotActiveException"/> when the account is not active when the session would be stored
/// (blocked or archived after login checked it); login then answers like any other refused login.
/// </summary>
public interface ISessionCreator
{
    Task<IssuedSession?> CreateAsync(Guid userId, AuthLevel level, CancellationToken ct = default);
}
