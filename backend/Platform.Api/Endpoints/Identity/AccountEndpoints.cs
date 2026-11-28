using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Enums;
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
        IEmailConfirmationService confirmation,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
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
                EmailDeliveryLogging.ConfirmationFailed(
                    loggerFactory.CreateLogger(nameof(AccountEndpoints)),
                    result.AccountId,
                    ex);
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