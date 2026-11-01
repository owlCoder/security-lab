using Microsoft.AspNetCore.Mvc;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

public sealed record LoginRequest(string? Email, string? Password);

[ApiController]
[Route("api/auth")]
public class LoginController(LoginAccount login) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await login.ExecuteAsync(body.Email, body.Password, HttpContext.TraceIdentifier, ct);
        Response.Headers.CacheControl = "no-store";

        switch (result.Outcome)
        {
            case LoginOutcome.Success:
                // The token goes only into the cookie, never into the body.
                Response.Cookies.Append(SessionCookie.Name, result.Session!.Token, SessionCookie.Options(result.Session.ExpiresAt));
                return Ok(new { userId = result.UserId });
            case LoginOutcome.InvalidRequest:
                return BadRequest(new { code = "invalid_request" });
            case LoginOutcome.Unavailable:
                return StatusCode(503, new { code = "login_unavailable" });
            default:
                return Unauthorized(new { code = "invalid_credentials" });
        }
    }
}
