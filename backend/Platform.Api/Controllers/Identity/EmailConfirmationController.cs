using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Identity.RateLimiting;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;

namespace Platform.Api.Controllers.Identity;

[DelegatedAuthorization]
[ApiController]
[Route("api/auth/email-confirmation")]
public sealed class EmailConfirmationController(
    IEmailConfirmationService confirmation,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<EmailConfirmationController> logger) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("request")]
    public async Task<IActionResult> RequestConfirmation([FromBody] EmailConfirmationRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var email = request?.Email?.Trim();
        if (!string.IsNullOrEmpty(email) && email.Length <= 254
            && MailAddress.TryCreate(email, out _))
        {
            var grant = await confirmation.IssueAsync(email, ct);
            if (grant is not null)
            {
                var baseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
                var link = $"{baseUrl}/confirm-email#{grant.Token}";
                try
                {
                    await emailSender.SendAsync(
                        grant.Email,
                        "Confirm your Security Lab email address",
                        $"Confirm your email address by opening this one-time link within 24 hours:\n\n{link}",
                        ct);
                }
                catch (EmailDeliveryException ex)
                {
                    logger.LogError(ex, "Email confirmation delivery failed.");
                }
            }
        }

        return Accepted(new { message = "If the account needs confirmation, an email will be sent." });
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmEmailRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        if (request?.Token is null || !await confirmation.ConfirmAsync(request.Token, ct))
            return BadRequest(new { code = "invalid_or_expired_confirmation" });

        return NoContent();
    }
}

public sealed record EmailConfirmationRequest(string? Email);
public sealed record ConfirmEmailRequest(string? Token);
