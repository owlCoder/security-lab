using Platform.Access.AspNet;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Api;

// LAB-01 HTTP sloj. Redosled za svaki zahtev:
//   1. SafeErrors          - neocekivana greska postaje genericki 500, bez stack trace-a
//   2. RequireLabAccess    - identitet (401) i uloga/poznata radnja (403), pre citanja tela
//   3. citanje i provera tela zahteva (400)
//   4. LaboratoryService   - vlasnistvo (404), domen i skladiste
public static class LaboratoriesEndpoints
{
    // Kanonska putanja (koriste je frontend i postojeci testovi).
    public const string BasePath = "/api/laboratories";

    // Kraci alias iz timskog plana. Isti handleri i iste provere; Location uvek pokazuje kanonsku putanju.
    public const string AliasPath = "/api/labs";

    // Naslov ima najvise 200 znakova; vece telo se ne parsira.
    private const long MaxBodyBytes = 4 * 1024;

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

        // POST /api/laboratories  ->  201 Created + Location + telo sa serverskim Id-em
        group.MapPost("/", async (HttpContext http, [FromServices] LaboratoryService service, [FromServices] IIdentityContext caller) =>
        {
            (bool ok, CreateLaboratoryRequest? request) = await TryReadBodyAsync<CreateLaboratoryRequest>(http);
            if (!ok)
            {
                return InvalidBody();
            }

            LabResult result = await service.CreateAsync(request?.Title, caller, http.RequestAborted);
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
        new(lab.Id, lab.Title, lab.Status.ToString(), lab.CreatedBy, lab.CreatedAt);

    private static IResult ToFailure(LabResultStatus status) => status switch
    {
        LabResultStatus.InvalidTitle => Results.BadRequest(new ApiErrorResponse(
            LabErrorCodes.InvalidTitle,
            $"Laboratory title is required and must not exceed {Laboratory.MaxTitleLength} characters.")),
        // Isti tekst za nepostojecu i tudju laboratoriju; bez ID-a i internih detalja.
        LabResultStatus.NotFound => Results.NotFound(new ApiErrorResponse(
            LabErrorCodes.NotFound, "Laboratory was not found.")),
        LabResultStatus.Unauthenticated => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthenticated"),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error"),
    };

    private static IResult InvalidBody() =>
        Results.BadRequest(new ApiErrorResponse(LabErrorCodes.InvalidBody, "Request body is invalid."));

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