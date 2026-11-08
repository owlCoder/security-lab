using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Enums;
using Platform.Identity.Models;
using Platform.Identity.Services;

// Oba modula imaju IAccessDecision; ovaj endpoint koristi Access modul.
using IAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Api.Endpoints.Identity;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // CreateAccountAsync calls the platform access decision before registration.
        app.MapPost("/api/admin/accounts", CreateAccountAsync)
            .DelegatesToLegacyAuthorization();
        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest? body,
        HttpContext context,
        IRequestSubjectResolver subjectResolver,
        IAccessDecision accessDecision,
        IAccountRegistration registration,
        IEmailConfirmationService confirmation,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
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

        if (result.Outcome == RegistrationOutcome.Created)
        {
            var grant = await confirmation.IssueAsync(body.Email!, cancellationToken);
            if (grant is null)
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Email confirmation could not be started.");

            var baseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var link = $"{baseUrl}/confirm-email#{grant.Token}";
            try
            {
                await emailSender.SendAsync(
                    grant.Email,
                    "Confirm your Security Lab email address",
                    $"Confirm your email address by opening this one-time link within 24 hours:\n\n{link}",
                    cancellationToken);
            }
            catch (EmailDeliveryException ex)
            {
                loggerFactory.CreateLogger(nameof(AccountEndpoints))
                    .LogError(ex, "Email confirmation delivery failed for account {AccountId}.", result.AccountId);
                return Results.Json(
                    new { error = "confirmation_email_unavailable", correlationId },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Created(
                $"/api/admin/accounts/{result.AccountId}",
                new { id = result.AccountId, correlationId, emailConfirmationRequired = true });
        }

        return result.Outcome switch
        {
            RegistrationOutcome.IdentifierTaken => Results.Conflict(
                new { error = "identifier_taken", correlationId }),
            _ => Results.BadRequest(
                new { error = "invalid_input", errors = result.Errors, correlationId })
        };
    }
}