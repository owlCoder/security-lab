using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

// ACC-05: both actions authenticate the caller themselves (401 without a session) and only
// ever touch the caller's own sessions, so the strict Access middleware delegates to them.
[DelegatedAuthorization]
[ApiController]
[Route("api/auth/sessions")]
public class SessionController(IIdentityContext identity, ISessionManager sessionManager) : ControllerBase
{
    private readonly IIdentityContext _identity = identity;
    private readonly ISessionManager _sessionManager = sessionManager;

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
