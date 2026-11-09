using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Contracts.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Services.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace Platform.Api.Controllers.Identity;

// ACC-05: login/logout are reachable without a session and /me checks the session itself,
// so the strict Access middleware delegates authorization to these handlers.
[DelegatedAuthorization]
[ApiController]
[Route("api/auth")]
public class AuthController(ILoginService login, ILogoutService logout, IIdentityContext identity) : ControllerBase
{
    private readonly IIdentityContext _identity = identity;
    private readonly ILoginService _login = login;
    private readonly ILogoutService _logout = logout;

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await _login.ExecuteAsync(body.Email, body.Password, HttpContext.TraceIdentifier, ct);
        Response.Headers.CacheControl = "no-store";

        switch (result.Outcome)
        {
            case LoginOutcome.Success:
                // The token goes only into the cookie, never into the body.
                Response.Cookies.Append(SessionCookie.Name, result.Session!.Token, SessionCookie.Options(result.Session.ExpiresAt));
                return Ok(new LoginResponse(result.UserId!.Value));
            case LoginOutcome.InvalidRequest:
                return BadRequest(new { code = "invalid_request" });
            case LoginOutcome.Unavailable:
                return StatusCode(503, new { code = "login_unavailable" });
            default:
                return Unauthorized(new { code = "invalid_credentials" });
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // Revoke first: if it fails, the cookie stays and the client can try again.
        // No request token: a closed tab or a broken connection must not leave the session valid.
        await _logout.ExecuteAsync(Request.Cookies[SessionCookie.Name], HttpContext.TraceIdentifier, CancellationToken.None);

        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var caller = _identity.Current;

        if (caller is not { IsAuthenticated: true })
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        return Ok(new MeResponse(caller.UserId!.Value, caller.Level.ToString()));
    }
}
