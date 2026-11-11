using Platform.Access.AspNet;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;

namespace Platform.Api.Modules.Laboratories.Api;

// LAB-01 HTTP sloj. Redosled za svaki zahtev:
//   1. SafeErrors          - neocekivana greska postaje genericki 500, bez stack trace-a
//   2. RequireLabAccess    - identitet (401) i uloga/poznata radnja (403), pre citanja tela
//   3. citanje i provera tela zahteva (400)
//   4. LaboratoryService   - kurs pri kreiranju (400/403, LAB-03), vlasnistvo (404), domen i skladiste
public static class LaboratoriesEndpoints
{
    // Kanonska putanja (koriste je frontend i postojeci testovi).
    public const string BasePath = "/api/laboratories";

    // Kraci alias iz timskog plana. Isti handleri i iste provere; Location uvek pokazuje kanonsku putanju.
    public const string AliasPath = "/api/labs";

    // Naslov ima najvise 200 znakova; vece telo se ne parsira.
    private const long MaxBodyBytes = 4 * 1024;

    public const string SearchQueryParameter = "q";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLaboratoriesEndpoints(this IEndpointRouteBuilder app)
    {
        MapRoutes(app.MapGroup(BasePath));
        MapRoutes(app.MapGroup(AliasPath));
        return app;
    }

    private static void MapRoutes(RouteGroupBuilder group)
    {
        // Every handler below has the existing RequireLabAccess filter, executed
        // before its body, and LaboratoryService additionally checks ownership.
        group.WithTags("Laboratories")
            .DelegatesToLegacyAuthorization()
            .AddEndpointFilter(SafeErrors);

        group.MapGet("/", () => Results.Ok(Array.Empty<object>()))
            .RequireLabAccess(LabActions.List);

        group.MapGet("/search", async (HttpContext http, [FromServices] LaboratorySearchService service, [FromServices] IIdentityContext caller) =>
        {
            http.Response.Headers.CacheControl = "no-store";

            IQueryCollection parameters = http.Request.Query;
            StringValues query = parameters[SearchQueryParameter];
            if (parameters.Keys.Any(key => key is not (SearchQueryParameter or "difficulty" or "type" or "tag"))
                || query.Count > 1
                || !TryReadEnum<LaboratoryDifficulty>(parameters["difficulty"], out var difficulty)
                || !TryReadEnum<LaboratoryType>(parameters["type"], out var type))
            {
                return InvalidQuery();
            }

            var filters = new LabSearchFilters(difficulty, type,
                parameters["tag"].Select(tag => tag ?? string.Empty).ToArray());
            LabSearchResult result = await service.SearchAsync(
                query.Count == 0 ? null : query[0], caller, http.RequestAborted, filters);
            return result.Status switch
            {
                LabSearchStatus.Ok => Results.Ok(new LaboratorySearchResponse(
                    result.Items.Select(ToSearchItem).ToList(), result.Truncated)),
                LabSearchStatus.InvalidQuery => InvalidQuery(),
                LabSearchStatus.Unauthenticated => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthenticated"),
                LabSearchStatus.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied"),
                LabSearchStatus.FiltersUnavailable => Results.Json(
                    new ApiErrorResponse(LabErrorCodes.FiltersUnavailable, "Laboratory filters are currently unavailable."),
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error"),
            };
        }).RequireLabAccess(LabActions.Search);

        group.MapGet("/search/filters", async (HttpContext http, [FromServices] LaboratorySearchService service, [FromServices] IIdentityContext caller) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            LabSearchFilterOptions options = await service.GetFilterOptionsAsync(caller, http.RequestAborted);
            return options.Status switch
            {
                LabSearchStatus.Ok => Results.Ok(new LaboratorySearchFiltersResponse(
                    options.Difficulties, options.Types, options.Tags, options.MetadataAvailable)),
                LabSearchStatus.Unauthenticated => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthenticated"),
                LabSearchStatus.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied"),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error"),
            };
        }).RequireLabAccess(LabActions.Search);

        // POST /api/laboratories  ->  201 Created + Location + telo sa serverskim Id-em
        group.MapPost("/", async (HttpContext http, [FromServices] LaboratoryService service, [FromServices] IIdentityContext caller) =>
        {
            (bool ok, CreateLaboratoryRequest? request) = await TryReadBodyAsync<CreateLaboratoryRequest>(http);
            if (!ok)
            {
                return InvalidBody();
            }

            LabResult result = await service.CreateAsync(request?.CourseId, request?.Title, caller, http.RequestAborted);
            return result.Status switch
            {
                LabResultStatus.Ok => Results.Created(
                    $"{BasePath}/{result.Laboratory!.Id}", ToResponse(result.Laboratory)),
                _ => ToFailure(result.Status),
            };
        }).RequireLabAccess(LabActions.Create);

        // GET /api/laboratories/{id}  ->  200 ili 404 (nepostojeca ili tudja, isti odgovor)
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, [FromServices] LaboratoryService service, [FromServices] IIdentityContext caller) =>
        {
            LabResult result = await service.GetAsync(id, caller, http.RequestAborted);
            return result.Status == LabResultStatus.Ok
                ? Results.Ok(ToResponse(result.Laboratory!))
                : ToFailure(result.Status);
        }).RequireLabAccess(LabActions.Read);

        // PATCH /api/laboratories/{id}/title  ->  200 sa istim Id-em i novim naslovom
        group.MapPatch("/{id:guid}/title", async (Guid id, HttpContext http, [FromServices] LaboratoryService service, [FromServices] IIdentityContext caller) =>
        {
            (bool ok, RenameLaboratoryRequest? request) = await TryReadBodyAsync<RenameLaboratoryRequest>(http);
            if (!ok)
            {
                return InvalidBody();
            }

            LabResult result = await service.RenameAsync(id, request?.Title, caller, http.RequestAborted);
            return result.Status == LabResultStatus.Ok
                ? Results.Ok(ToResponse(result.Laboratory!))
                : ToFailure(result.Status);
        }).RequireLabAccess(LabActions.Rename);
    }

    private static LaboratoryResponse ToResponse(Laboratory lab) =>
        new(lab.Id, lab.CourseId, lab.Title, lab.Status.ToString(), lab.CreatedBy, lab.CreatedAt);

    private static LaboratorySearchItem ToSearchItem(Laboratory lab) =>
        new(lab.Id, lab.Title, lab.Status.ToString(), lab.CreatedAt);

    private static IResult ToFailure(LabResultStatus status) => status switch
    {
        LabResultStatus.InvalidTitle => Results.BadRequest(new ApiErrorResponse(
            LabErrorCodes.InvalidTitle,
            $"Laboratory title is required and must not exceed {Laboratory.MaxTitleLength} characters.")),
        LabResultStatus.InvalidCourse => Results.BadRequest(new ApiErrorResponse(
            LabErrorCodes.InvalidCourse, "Laboratory course is required.")),
        // LAB-03: isti odgovor kao AccessCheckFilter; ne otkriva da li kurs postoji ni koju ulogu korisnik ima.
        LabResultStatus.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied"),
        // Isti tekst za nepostojecu i tudju laboratoriju; bez ID-a i internih detalja.
        LabResultStatus.NotFound => Results.NotFound(new ApiErrorResponse(
            LabErrorCodes.NotFound, "Laboratory was not found.")),
        LabResultStatus.Unauthenticated => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthenticated"),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error"),
    };

    private static IResult InvalidBody() =>
        Results.BadRequest(new ApiErrorResponse(LabErrorCodes.InvalidBody, "Request body is invalid."));

    private static IResult InvalidQuery() =>
        Results.BadRequest(new ApiErrorResponse(
            LabErrorCodes.InvalidQuery,
            $"Search text (q) must not exceed {LabTitleSearch.MaxQueryLength} characters or contain control characters; filter values must be valid."));

    private static bool TryReadEnum<T>(StringValues values, out T? value) where T : struct, Enum
    {
        value = null;
        if (values.Count == 0) return true;
        if (values.Count != 1 || values[0] is not string text) return false;
        text = text.Trim();
        if (!Enum.GetNames<T>().Contains(text, StringComparer.OrdinalIgnoreCase)) return false;
        value = Enum.Parse<T>(text, ignoreCase: true);
        return true;
    }

    // Telo se cita rucno (ne kroz automatsko vezivanje parametara) da bi:
    //  - provera prava (filter) bila pre parsiranja nepouzdanog unosa,
    //  - neispravan JSON uvek dao kontrolisan 400, i u Development rezimu bez stack trace-a.
    private static async Task<(bool Ok, T? Value)> TryReadBodyAsync<T>(HttpContext http) where T : class
    {
        HttpRequest request = http.Request;

        if (!request.HasJsonContentType())
        {
            return (false, null);
        }

        if (request.ContentLength is > MaxBodyBytes)
        {
            return (false, null);
        }

        try
        {
            using var limited = new MemoryStream();
            byte[] buffer = new byte[1024];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
            {
                if (limited.Length + read > MaxBodyBytes)
                {
                    return (false, null);
                }

                limited.Write(buffer, 0, read);
            }

            limited.Position = 0;
            T? value = await JsonSerializer.DeserializeAsync<T>(limited, JsonOptions, http.RequestAborted);
            return (value is not null, value);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    // Poslednja linija odbrane: neocekivani izuzetak se belezi na serveru, a klijent dobija
    // samo genericku poruku i ID zahteva za podrsku (bez tipa izuzetka i stack trace-a).
    private static async ValueTask<object?> SafeErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HttpContext http = context.HttpContext;
            http.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Laboratories.Api")
                .LogError(ex, "Unhandled error in laboratories endpoint, traceId={TraceId}", http.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error",
                extensions: new Dictionary<string, object?> { ["traceId"] = http.TraceIdentifier });
        }
    }
}
