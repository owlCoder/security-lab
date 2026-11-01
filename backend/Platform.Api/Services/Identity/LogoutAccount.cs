using Platform.Identity.Services;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-05: revokes the session of the presented cookie (DFD process P4). It works for Active and MfaPending
/// sessions and only touches this one session. Without a session to revoke it does nothing, so the caller
/// answers the same in every case and learns nothing about the session state.
/// </summary>
public sealed class LogoutAccount(ISessionStore sessions, IAuditWriter audit, TimeProvider clock)
{
    private const string AuditAction = "auth.logout";

    public async Task ExecuteAsync(string? rawToken, string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken))
            return;

        var session = await sessions.RevokeByTokenAsync(rawToken, ct);
        if (session is null)
            return;

        // Never put the token or its hash in an audit event (AUD-03). The handle is public.
        audit.Write(new AuditEvent(clock.GetUtcNow(), session.UserId, AuditAction, session.SessionHandle.ToString(), "success", correlationId, null));
    }
}
