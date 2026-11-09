using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.AssignmentOverview.Application;
using Platform.AssignmentOverview.Contracts;
using Platform.AssignmentOverview.Domain;
using Platform.AssignmentOverview.Infrastructure;

namespace Platform.Api.Endpoints;

public sealed record MyAssignmentResponse(
    Guid AssignmentId,
    string CourseId,
    string PublishedLabRevisionId,
    DateTimeOffset? AvailableFromUtc,
    DateTimeOffset DueAtUtc,
    int MaxAttempts,
    bool IsLocked,
    IReadOnlyList<Guid> MissingAssignmentIds);

public sealed record MyAssignmentsResponse(
    IReadOnlyList<MyAssignmentResponse> Assignments,
    string CorrelationId);

public sealed record MyAssignmentsErrorResponse(string Error, string CorrelationId);

/// <summary>
/// ASN-02: dodele prijavljenog studenta. Student se uzima samo iz sesije, nikad iz zahteva.
/// </summary>
public static class MyAssignmentsEndpoints
{
    public const string Route = "/api/me/assignments";

    public static IEndpointRouteBuilder MapMyAssignmentsEndpoints(this IEndpointRouteBuilder app)
    {
        // Bez AllowAnonymous: SessionCheckMiddleware mora da upise sesiju, inace IIdentityContext ne vidi pozivaoca.
        app.MapGet(Route, GetAsync)
            .DelegatesToLegacyAuthorization();
        return app;
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        IIdentityContext identity,
        [FromServices] IMyAssignmentsService service,
        [FromServices] AssignmentLockService locks,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        context.Response.Headers.CacheControl = "no-store";

        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is null)
        {
            return Results.Json(
                new MyAssignmentsErrorResponse("unauthenticated", correlationId),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var studentId = caller.UserId.Value.ToString();

        IReadOnlyList<StudentAssignmentView> assignments;
        IReadOnlyDictionary<Guid, AssignmentLockStatus> lockStatuses;

        try
        {
            assignments = await service.GetForStudentAsync(
                studentId,
                cancellationToken);

            // ASN-07: zaključanost računa server; klijent je samo prikazuje.
            lockStatuses = await locks.GetLockStatusesAsync(
                studentId,
                assignments,
                cancellationToken);
        }
        catch (DependencyUnavailableException ex)
        {
            // Fail-closed, ali kontrolisano: bez podataka, bez detalja o grešci, sa ID-jem za log.
            loggerFactory.CreateLogger("MyAssignments").LogWarning(
                "My assignments unavailable: {Contract} is not connected. CorrelationId {CorrelationId}",
                ex.Contract,
                correlationId);

            return Results.Json(
                new MyAssignmentsErrorResponse("service_unavailable", correlationId),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new MyAssignmentsResponse(
            assignments
                .Select(a => new MyAssignmentResponse(
                    a.AssignmentId,
                    a.CourseId,
                    a.PublishedLabRevisionId,
                    a.AvailableFromUtc,
                    a.DueAtUtc,
                    a.MaxAttempts,
                    lockStatuses[a.AssignmentId].IsLocked,
                    lockStatuses[a.AssignmentId].MissingAssignmentIds))
                .ToList(),
            correlationId));
    }
}