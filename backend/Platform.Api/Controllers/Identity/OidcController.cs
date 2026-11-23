using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Platform.Access.AspNet;
using Platform.Api.Modules.Identity.Federation;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Security.IdentityAccess;

namespace Platform.Api.Controllers.Identity;

/// <summary>IDN-08: starts the OIDC login. The callback is handled by the OIDC middleware, not by a controller.</summary>
[ApiController]
[Route("api/auth/oidc")]
public sealed class OidcController(IOptions<OidcFederationOptions> options) : ControllerBase
{
    [AllowAnonymous]
    [RequireAccess(IdentityAccessActions.Login)]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpGet("start")]
    public IActionResult Start()
    {
        if (!options.Value.Enabled)
            return NotFound();

        Response.Headers.CacheControl = "no-store";

        // The target after login is fixed by configuration, not taken from the request (no open redirect).
        return Challenge(
            new AuthenticationProperties { RedirectUri = options.Value.SuccessRedirect },
            OidcFederationExtensions.Scheme);
    }
}