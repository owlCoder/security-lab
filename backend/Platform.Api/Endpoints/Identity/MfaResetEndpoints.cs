using Platform.Access.AspNet;
using Platform.Api.Enums.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Security.IdentityAccess;
using Platform.Api.Services.Identity;

namespace Platform.Api.Endpoints.Identity;

public static class MfaResetEndpoints
{
    public static IEndpointRouteBuilder MapMfaResetEndpoints(this IEndpointRouteBuilder app)
    {
        // ACC-05: the central Access middleware checks the action before the handler runs.
        app.MapPost("/api/admin/accounts/{id:guid}/mfa/reset", ResetMfaAsync)
            .RequireAccess(IdentityAccessActions.MfaReset);
        return app;
    }

    private static async Task<IResult> ResetMfaAsync(
        Guid id,
        ResetMfaRequest? body,
        HttpContext context,
        ResetMfa resetMfa,
        CancellationToken cancellationToken)
    {
        var correlationId = context.TraceIdentifier;
        var result = await resetMfa.ExecuteAsync(id, body?.Reason, cancellationToken);

        return result.Outcome switch
        {
            ResetMfaOutcome.Success => Results.Ok(new
            {
                id,
                mfaReset = true,
                mfaWasEnabled = result.MfaWasEnabled,
                sessionsRevoked = result.SessionsRevoked,
                correlationId
            }),
            ResetMfaOutcome.Unauthenticated => Error(StatusCodes.Status401Unauthorized, "unauthenticated", correlationId),
            ResetMfaOutcome.Forbidden => Error(StatusCodes.Status403Forbidden, "forbidden", correlationId),
            ResetMfaOutcome.InvalidReason => Error(StatusCodes.Status400BadRequest, "invalid_reason", correlationId),
            ResetMfaOutcome.NotFound => Error(StatusCodes.Status404NotFound, "account_not_found", correlationId),
            ResetMfaOutcome.SelfChangeNotAllowed => Error(StatusCodes.Status409Conflict, "self_change_not_allowed", correlationId),
            _ => Error(StatusCodes.Status503ServiceUnavailable, "unavailable", correlationId)
        };
    }

    private static IResult Error(int status, string error, string correlationId) =>
        Results.Json(new { error, correlationId }, statusCode: status);
}
