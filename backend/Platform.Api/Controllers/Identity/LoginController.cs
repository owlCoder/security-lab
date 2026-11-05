using Platform.Access.AspNet;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

public sealed record LoginRequest(string? Email, string? Password);

[ApiController]
[Route("api/auth")]
public sealed class LoginController(LoginAccount login) : ControllerBase
{
    [DelegatedAuthorization]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var result = login.Execute(request.Email, request.Password);

        return result.Outcome switch
        {
            LoginOutcome.Success => Ok(new
            {
                tokenType = "Bearer",
                accessToken = result.AccessToken
            }),
            LoginOutcome.InvalidCredentials => Unauthorized(new
            {
                code = "invalid_credentials",
                message = "Погрешан имејл или лозинка."
            }),
            LoginOutcome.AccountBlocked => StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "account_blocked",
                message = "Налог је блокиран."
            }),
            LoginOutcome.AccountArchived => StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "account_archived",
                message = "Налог је архивиран."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "login_unavailable",
                message = "Пријава тренутно није доступна."
            })
        };
    }
}
