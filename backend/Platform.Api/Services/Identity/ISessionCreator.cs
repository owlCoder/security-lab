using Platform.Api.Domain.Authentication;

namespace Platform.Api.Services.Identity;

/// <param name="Token">Opaque secret for the cookie. The store must keep only a hash of it.</param>
/// <param name="Handle">Public handle of the session (same one that appears in SessionInfo).</param>
public sealed record IssuedSession(string Token, Guid Handle, DateTimeOffset ExpiresAt);

/// <summary>
/// What login needs from the session store (US-03). ISessionManager has no "create", so this is the
/// proposed contract. Every call must produce a NEW token (session fixation protection).
/// Return null when the store cannot create a session; login then answers 503.
/// </summary>
public interface ISessionCreator
{
    Task<IssuedSession?> CreateAsync(Guid userId, AuthenticationLevel level, CancellationToken ct = default);
}

/// <summary>Session cookie settings, shared by login (sets it), logout (US-05) and session check (US-06).</summary>
public static class SessionCookie
{
    public const string Name = "SecurityLab.Session";

    public static CookieOptions Options(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true
    };
}
