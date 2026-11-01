using Microsoft.AspNetCore.Mvc;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

[ApiController]
[Route("api/auth")]
public class MeController : ControllerBase
{
    private readonly IIdentityContext _identity;

    public MeController(IIdentityContext identity)
    {
        _identity = identity;
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var caller = _identity.Current;

        if (caller == null || !caller.IsAuthenticated)
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        return Ok(new { userId = caller.UserId, level = caller.Level.ToString() });
    }
}