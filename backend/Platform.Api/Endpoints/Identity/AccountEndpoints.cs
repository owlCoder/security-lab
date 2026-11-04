using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

// Oba modula imaju IAccessDecision; ovaj endpoint koristi Access modul.
using IAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Api.Endpoints.Identity;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/accounts", CreateAccountAsync);
        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest? body,
        HttpContext context,
        IRequestSubjectResolver subjectResolver,
        IAccessDecision accessDecision,
        IAccountRegistration registration,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                subjectResolver.Resolve(context),
                CreateAccountPolicy.ActionId,
                null,
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return decision.DenialReason == AccessDenialReason.Unauthenticated
                ? Results.Json(new { error = "unauthenticated", correlationId },
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Json(new { error = "forbidden", correlationId },
                    statusCode: StatusCodes.Status403Forbidden);
        }

        if (body is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_input",
                errors = new[] { "Telo zahteva je obavezno." },
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