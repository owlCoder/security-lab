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
    public async Task<IActionResult> Logout()
    {
        // Revoke first: if it fails, the cookie stays and the client can try again.
        // No request token: a closed tab or a broken connection must not leave the session valid.
        await logout.ExecuteAsync(Request.Cookies[SessionCookie.Name], HttpContext.TraceIdentifier, CancellationToken.None);

        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(DateTimeOffset.UnixEpoch));
        return NoContent();
    }
}
