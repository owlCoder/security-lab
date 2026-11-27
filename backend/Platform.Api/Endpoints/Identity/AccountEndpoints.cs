using Platform.Access.AspNet;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Api.Endpoints.Identity;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/accounts", CreateAccountAsync)
            .RequireAccess(CreateAccountPolicy.ActionId);
        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest? body,
        HttpContext context,
        IAccountRegistration registration,
        CancellationToken cancellationToken)
    {
        var correlationId = context.TraceIdentifier;

        if (body is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_input",
                errors = new[] { "Request body is required" },
                correlationId
            });
        }

        var result = await registration.RegisterAsync(
            new RegisterAccountCommand(body.Email ?? string.Empty, body.Password ?? string.Empty),
            cancellationToken);

        return result.Outcome switch
        {
            RegistrationOutcome.Created => Results.Created(
                $"/api/admin/accounts/{result.AccountId}",
                new { id = result.AccountId, correlationId }),
            RegistrationOutcome.IdentifierTaken => Results.Conflict(
                new { error = "identifier_taken", correlationId }),
            _ => Results.BadRequest(
                new { error = "invalid_input", errors = result.Errors, correlationId })
        };
    }
}