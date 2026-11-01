using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

[ApiController]
[Route("api/auth")]
public class LogoutController(LogoutAccount logout) : ControllerBase
{
    // Anonymous on purpose: logout without a valid session is idempotent and must not become a 401.
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Revoke first: if it fails, the cookie stays and the client can try again.
        await logout.ExecuteAsync(Request.Cookies[SessionCookie.Name], HttpContext.TraceIdentifier, ct);

        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(DateTimeOffset.UnixEpoch));
        return NoContent();
    }
}
