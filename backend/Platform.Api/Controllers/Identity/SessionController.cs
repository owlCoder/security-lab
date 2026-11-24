using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Security.IdentityAccess;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

// ACC-05 / IDN-07: each action is gated by the central Access middleware (RequireAccess, deny by default).
// The subject comes from the server-side session (IdentitySessionSubjectProvider), so a caller can only
// ever list or revoke their own sessions; the handlers repeat the 401 check as a second line of defence.
[ApiController]
[Route("api/auth/sessions")]
public class SessionController(IIdentityContext identity, ISessionManager sessionManager) : ControllerBase
{
    private readonly IIdentityContext _identity = identity;
    private readonly ISessionManager _sessionManager = sessionManager;

    [RequireAccess(IdentityAccessActions.SessionList)]
    [HttpGet]
    public async Task<IActionResult> GetMySessions(CancellationToken ct)
    {
        var caller = _identity.Current;

        if (caller == null || !caller.IsAuthenticated || caller.UserId == null)
            return Unauthorized(new { code = "unauthenticated" });

        var sessions = await _sessionManager.ListForUser(caller.UserId.Value, ct);

        Response.Headers.CacheControl = "no-store";

        return Ok(sessions);
    }

    [RequireAccess(IdentityAccessActions.SessionRevoke)]
    [HttpDelete("{handle:guid}")]
    public async Task<IActionResult> RevokeSession(Guid handle, CancellationToken ct)
    {
        var caller = _identity.Current;

        if (caller == null || !caller.IsAuthenticated || caller.UserId == null)
            return Unauthorized(new { code = "unauthenticated" });

        var revoked = await _sessionManager.RevokeSession(caller.UserId.Value, handle, ct);

        Response.Headers.CacheControl = "no-store";

        if (!revoked)
            return NotFound(new { code = "session_not_found" });

        return NoContent();
    }
}
