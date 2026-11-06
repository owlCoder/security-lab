using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Interfaces;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-05: revokes the session of the presented cookie (DFD process P4). It works for Active and MfaPending
/// sessions and only touches this one session. Without a session to revoke it does nothing, so the caller
/// answers the same in every case and learns nothing about the session state.
/// </summary>
internal sealed class LogoutService(
    ISessionStore sessions,
    IdentityAuditProducer audit) : ILogoutService
{
    public async Task ExecuteAsync(string? rawToken, string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken))
            return;

        var session = await sessions.RevokeByTokenAsync(rawToken, ct);
        if (session is null)
            return;

        // Logout remains effective even if its audit record is unavailable. The producer
        // raises a technical alarm without including the token or session identifier.
        await audit.LogoutSucceededAsync(
            session.UserId, correlationId, CancellationToken.None);
    }
}
