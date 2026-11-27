using Platform.Access.AspNet;
using Platform.Api.Modules.Competencies;
using Platform.Competencies.Domain;

namespace Platform.Api.Endpoints;

public sealed record CompetencyResponse(Guid Id, string Name, string? Description);
public sealed record CompetencyCatalogResponse(IReadOnlyList<CompetencyResponse> Competencies);
public sealed record LaboratoryCompetenciesResponse(
    Guid LaboratoryId,
    IReadOnlyList<CompetencyResponse> Competencies,
    string LaboratoryTitle,
    bool CanManage);
public sealed record CompetencyErrorResponse(string Error);

// PRG-04 only. Identity, laboratory existence and operation-specific authorization
// are enforced before a store is accessed. Existing LAB/ACC endpoints are untouched.
public static class CompetencyEndpoints
{
    public const string CatalogPath = "/api/competencies";
    public const string LaboratoryCompetenciesPath = "/api/laboratories/{laboratoryId:guid}/competencies";

    public static IEndpointRouteBuilder MapCompetencyEndpoints(this IEndpointRouteBuilder app)
    {
        Configure(app.MapGet(CatalogPath, ListCatalogAsync));
        Configure(app.MapGet(LaboratoryCompetenciesPath, ListLaboratoryCompetenciesAsync));
        Configure(app.MapPut($"{LaboratoryCompetenciesPath}/{{competencyId:guid}}", AddLaboratoryCompetencyAsync));
        Configure(app.MapDelete($"{LaboratoryCompetenciesPath}/{{competencyId:guid}}", RemoveLaboratoryCompetencyAsync));
        return app;
    }

    public static async Task<IResult> ListCatalogAsync(
        HttpContext http, Prg04SessionIdentity identity, CompetencyLinkService operations, CancellationToken ct)
    {
        if (await identity.ResolveAsync(http, ct) is null) return Unauthenticated();
        var result = await operations.CatalogAsync(ct);
        return result.Status == CompetencyOperationStatus.Ok
            ? Results.Ok(new CompetencyCatalogResponse(ToResponses(result.Competencies!))) : Failure(result.Status);
    }

    public static async Task<IResult> ListLaboratoryCompetenciesAsync(
        Guid laboratoryId, HttpContext http, Prg04SessionIdentity identity,
        CompetencyLinkService operations, CancellationToken ct)
    {
        if (await identity.ResolveAsync(http, ct) is not { } userId) return Unauthenticated();
        var result = await operations.ReadAsync(userId, laboratoryId, ct);
        return result.Status == CompetencyOperationStatus.Ok
            ? Results.Ok(new LaboratoryCompetenciesResponse(laboratoryId, ToResponses(result.Competencies!),
                result.LaboratoryTitle!, result.CanManage))
            : Failure(result.Status);
    }

    public static Task<IResult> AddLaboratoryCompetencyAsync(
        Guid laboratoryId, Guid competencyId, HttpContext http, Prg04SessionIdentity identity,
        CompetencyLinkService operations, CancellationToken ct) =>
        ChangeAsync(laboratoryId, competencyId, http, identity, operations, add: true, ct);

    public static Task<IResult> RemoveLaboratoryCompetencyAsync(
        Guid laboratoryId, Guid competencyId, HttpContext http, Prg04SessionIdentity identity,
        CompetencyLinkService operations, CancellationToken ct) =>
        ChangeAsync(laboratoryId, competencyId, http, identity, operations, add: false, ct);

    private static async Task<IResult> ChangeAsync(
        Guid laboratoryId, Guid competencyId, HttpContext http, Prg04SessionIdentity identity,
        CompetencyLinkService operations, bool add, CancellationToken ct)
    {
        if (await identity.ResolveAsync(http, ct) is not { } userId) return Unauthenticated();
        var result = await operations.ChangeAsync(userId, laboratoryId, competencyId, add, ct);
        return result.Status switch
        {
            CompetencyOperationStatus.Created => Results.Created(
                $"/api/laboratories/{laboratoryId:D}/competencies/{competencyId:D}", value: null),
            CompetencyOperationStatus.NoContent => Results.NoContent(),
            _ => Failure(result.Status)
        };
    }

    private static RouteHandlerBuilder Configure(RouteHandlerBuilder endpoint) => endpoint
        .WithTags("Competencies")
        // Central entry authorization uses the validated PRG-04 session. Concrete
        // laboratory visibility and Manage authorization retain their 404 contract.
        .RequireAccess(CompetencyLinkActions.Entry)
        .AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                return await next(invocation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var http = invocation.HttpContext;
                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Prg04.Competencies")
                    .LogError(ex, "PRG-04 operation failed, traceId={TraceId}", http.TraceIdentifier);
                return Results.Problem(statusCode: 503, title: "Competency operation unavailable");
            }
        });

    private static IResult Unauthenticated() => Error(401, "unauthenticated");

    private static IResult Failure(CompetencyOperationStatus status) => status switch
    {
        CompetencyOperationStatus.InvalidLaboratoryId => Error(400, "invalid_laboratory_id"),
        CompetencyOperationStatus.InvalidCompetencyId => Error(400, "invalid_competency_id"),
        CompetencyOperationStatus.LaboratoryNotFound => Error(404, "laboratory_not_found"),
        CompetencyOperationStatus.CompetencyNotFound => Error(404, "competency_not_found"),
        _ => Error(503, "competency_storage_unavailable")
    };

    private static IResult Error(int status, string error) =>
        Results.Json(new CompetencyErrorResponse(error), statusCode: status);

    private static IReadOnlyList<CompetencyResponse> ToResponses(IReadOnlyList<Competency> competencies) =>
        competencies.Select(x => new CompetencyResponse(x.Id, x.Name, x.Description)).ToList();
}

