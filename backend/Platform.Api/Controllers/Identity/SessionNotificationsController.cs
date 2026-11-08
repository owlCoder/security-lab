using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Interfaces;

namespace Platform.Api.Controllers.Identity;

[DelegatedAuthorization]
[ApiController]
[Route("api/auth/session-notifications")]
public sealed class SessionNotificationsController(
    Platform.Api.Interfaces.Identity.IIdentityContext identity,
    ISessionNotificationPreferences preferences) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (identity.Current is not { IsAuthenticated: true, UserId: { } userId })
            return Unauthorized();

        Response.Headers.CacheControl = "no-store";
        var enabled = await preferences.GetAsync(userId, ct);
        return enabled is null ? NotFound() : Ok(new SessionNotificationPreferenceResponse(enabled.Value));
    }

    [HttpPut]
    public async Task<IActionResult> Set(
        [FromBody] SessionNotificationPreferenceRequest? request,
        CancellationToken ct)
    {
        if (identity.Current is not { IsAuthenticated: true, UserId: { } userId })
            return Unauthorized();
        if (request is null)
            return BadRequest(new { code = "invalid_request" });

        Response.Headers.CacheControl = "no-store";
        if (!await preferences.SetAsync(userId, request.Enabled, ct))
            return NotFound();

        return Ok(new SessionNotificationPreferenceResponse(request.Enabled));
    }
}

public sealed record SessionNotificationPreferenceRequest(bool Enabled);
public sealed record SessionNotificationPreferenceResponse(bool Enabled);
