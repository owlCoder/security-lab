using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Contracts.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Services.Identity;
using Platform.Api.Security.IdentityAccess;
using Microsoft.AspNetCore.RateLimiting;
using IIdentityContext = Platform.Api.Interfaces.Identity.IIdentityContext;

namespace Platform.Api.Controllers.Identity;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ILoginService login,
    ILogoutService logout,
    IIdentityContext identity,
    IMfaService mfaService) : ControllerBase
{
    private readonly IIdentityContext _identity = identity;
    private readonly ILoginService _login = login;
    private readonly ILogoutService _logout = logout;
    private readonly IMfaService _mfaService = mfaService;

    [AllowAnonymous]
    [RequireAccess(IdentityAccessActions.Login)]
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
                // US-15: the CSRF token is bound to the session, so the old one stops working now. Issue one for
                // the new session at once, so the next request does not start with a 403 and a retry.
                if (HttpContext.RequestServices?.GetService<IAntiforgery>() is { } antiforgery)
                {
                    HttpContext.Items[CsrfSessionBinding.SessionOverrideKey] = result.Session.Token;
                    CsrfToken.Issue(HttpContext, antiforgery);
                }
                return Ok(new LoginResponse(result.UserId!.Value, result.MfaRequired));
            case LoginOutcome.InvalidRequest:
                return BadRequest(new { code = "invalid_request" });
            case LoginOutcome.Unavailable:
                return StatusCode(503, new { code = "login_unavailable" });
            default:
                return Unauthorized(new { code = "invalid_credentials" });
        }
    }

    // US-15: the frontend calls this when the app starts and after a 403 csrf_invalid. The request token is bound
    // to the session cookie of this request (CsrfSessionBinding), returned and also put in a readable cookie.
    [AllowAnonymous]
    [RequireAccess(IdentityAccessActions.CsrfToken)]
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)
    {
        var token = CsrfToken.Issue(HttpContext, antiforgery);

        Response.Headers.CacheControl = "no-store";
        return Ok(new CsrfTokenResponse(token));
    }

    [AllowAnonymous]
    [RequireAccess(IdentityAccessActions.Logout)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // Revoke first: if it fails, the cookie stays and the client can try again.
        // No request token: a closed tab or a broken connection must not leave the session valid.
        await _logout.ExecuteAsync(Request.Cookies[SessionCookie.Name], HttpContext.TraceIdentifier, CancellationToken.None);

        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(DateTimeOffset.UnixEpoch));
        // US-15: the pair belonged to the ended session; the next user of this browser gets a new one.
        CsrfToken.Clear(Response);
        return NoContent();
    }

    [RequireAccess(IdentityAccessActions.SessionRead)]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var caller = _identity.Current;

        if (caller is not { IsAuthenticated: true })
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        return Ok(new MeResponse(caller.UserId!.Value, caller.Level.ToString()));
    }

    [RequireAccess(IdentityAccessActions.MfaEnroll)]
    [HttpPost("mfa/enroll")]
    public async Task<IActionResult> MfaEnroll(CancellationToken ct)
    {
        var (outcome, uri) =
            await _mfaService.EnrollAsync(Request.Cookies[SessionCookie.Name], HttpContext.TraceIdentifier, ct);
        Response.Headers.CacheControl = "no-store";
        return outcome switch
        {
            MfaOutcome.Success => Ok(new MfaEnrollResponse(uri!)),
            MfaOutcome.AlreadyEnabled => Conflict(new { code = "mfa_already_enabled" }),
            MfaOutcome.EnrollmentPending => Conflict(new { code = "mfa_enrollment_pending" }),
            MfaOutcome.Unavailable => StatusCode(503, new { code = "mfa_unavailable" }),
            _ => Unauthorized(new { code = "invalid_mfa" })
        };
    }

    [RequireAccess(IdentityAccessActions.MfaEnrollConfirm)]
    [HttpPost("mfa/enroll/confirm")]
    public async Task<IActionResult> MfaEnrollConfirm([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var outcome = await _mfaService.ConfirmEnrollAsync(Request.Cookies[SessionCookie.Name], request.Code,
            HttpContext.TraceIdentifier, ct);
        Response.Headers.CacheControl = "no-store";
        return outcome switch
        {
            MfaOutcome.Success => Ok(),
            MfaOutcome.InvalidRequest => BadRequest(new { code = "invalid_request" }),
            MfaOutcome.Unavailable => StatusCode(503, new { code = "mfa_unavailable" }),
            _ => Unauthorized(new { code = "invalid_mfa" })
        };
    }

    [RequireAccess(IdentityAccessActions.MfaVerify)]
    [HttpPost("mfa/verify")]
    public async Task<IActionResult> MfaVerify([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var (outcome, session) = await _mfaService.VerifyAsync(Request.Cookies[SessionCookie.Name], request.Code,
            HttpContext.TraceIdentifier, ct);
        Response.Headers.CacheControl = "no-store";
        if (outcome == MfaOutcome.Success)
        {
            // IDN-11: the pending session is revoked, so the cookie gets the token of the new active session.
            Response.Cookies.Append(SessionCookie.Name, session!.Token, SessionCookie.Options(session.ExpiresAt));
            // US-15: the CSRF token is bound to the session, so the one of the pending session stops working now.
            // Issue one for the new session at once, as Login does, so the next request does not get a 403.
            if (HttpContext.RequestServices?.GetService<IAntiforgery>() is { } antiforgery)
            {
                HttpContext.Items[CsrfSessionBinding.SessionOverrideKey] = session.Token;
                CsrfToken.Issue(HttpContext, antiforgery);
            }
            return Ok();
        }

        return outcome switch
        {
            MfaOutcome.InvalidRequest => BadRequest(new { code = "invalid_request" }),
            MfaOutcome.Unavailable => StatusCode(503, new { code = "mfa_unavailable" }),
            _ => Unauthorized(new { code = "invalid_mfa" })
        };
    }
}
