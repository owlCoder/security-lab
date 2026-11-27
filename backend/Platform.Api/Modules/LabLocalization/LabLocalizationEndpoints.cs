using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Services.Identity;
using Platform.LabLocalization.Application;
using Platform.LabLocalization.Domain;
// Unutar ovog namespace-a ime LabLocalization označava namespace, ne domensku klasu.
using LabTranslation = Platform.LabLocalization.Domain.LabLocalization;

namespace Platform.Api.Modules.LabLocalization;

public static class LabLocalizationEndpoints
{
    public const string LanguageQueryParameter = "lang";

    private const long MaxBodyBytes = 128 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLabLocalizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(LaboratoriesEndpoints.BasePath + "/{labId:guid}")
            .WithTags("Laboratory localization")
            .RequireAccess(LabLocalizationActions.Entry)
            .AddEndpointFilter(SafeErrors);

        group.MapGet("/content", async (
            Guid labId,
            HttpContext http,
            [FromServices] LabCaller callers,
            [FromServices] ILocalizationAccess access,
            [FromServices] GetLabContent getContent) =>
        {
            http.Response.Headers.CacheControl = "no-store";

            if (await ResolveCallerAsync(callers, http.RequestAborted) is not { } actorId)
            {
                return Unauthenticated();
            }

            // Odbijeno čitanje daje isti odgovor kao nepostojeća laboratorija.
            if (!await access.IsAllowedAsync(actorId, LocalizationAction.ReadContent, labId, http.RequestAborted))
            {
                return NotFound();
            }

            StringValues language = http.Request.Query[LanguageQueryParameter];
            if (language.Count > 1)
            {
                return Failure(LocalizationErrorCodes.InvalidLanguage);
            }

            try
            {
                LabContentView content = await getContent.ExecuteAsync(
                    labId, language.Count == 0 ? null : language[0], http.RequestAborted);

                return Results.Ok(new LabContentResponse(
                    content.RevisionId,
                    content.EvaluatorVersion,
                    ToCode(content.Language),
                    content.Title,
                    content.Description,
                    content.Instructions));
            }
            catch (DomainRuleException ex)
            {
                return Failure(ex.Code);
            }
        });

        group.MapPost("/revisions/{revisionId:guid}/translations", async (
            Guid labId,
            Guid revisionId,
            HttpContext http,
            [FromServices] LabCaller callers,
            [FromServices] AddLabLocalization addTranslation) =>
        {
            if (await ResolveCallerAsync(callers, http.RequestAborted) is not { } actorId)
            {
                return Unauthenticated();
            }

            (bool ok, AddTranslationRequest? request) = await TryReadBodyAsync(http);
            if (!ok || request is null)
            {
                return InvalidBody();
            }

            try
            {
                LabTranslation translation = await addTranslation.ExecuteAsync(
                    actorId,
                    labId,
                    revisionId,
                    request.Language,
                    request.Title,
                    request.Description,
                    request.Instructions,
                    http.RequestAborted);

                // Bez Location zaglavlja: prevod pripada nacrtu, a GET /content vraća samo trenutno objavljenu reviziju.
                return Results.Created((string?)null, new LabTranslationResponse(
                    translation.RevisionId,
                    ToCode(translation.Language),
                    translation.Title,
                    translation.Description,
                    translation.Instructions));
            }
            catch (DomainRuleException ex)
            {
                return Failure(ex.Code);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));

        return app;
    }

    private static async Task<Guid?> ResolveCallerAsync(LabCaller callers, CancellationToken ct)
    {
        var subject = await callers.GetSubjectAsync(ct);
        return subject is { State: AccountState.Active } && Guid.TryParse(subject.Id, out var id) && id != Guid.Empty
            ? id
            : null;
    }

    private static string ToCode(LabLanguage language) => language switch
    {
        LabLanguage.Sr => "sr",
        LabLanguage.En => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    private static IResult Failure(string code) => code switch
    {
        LocalizationErrorCodes.InvalidLanguage => BadRequest(code,
            "Language must be 'sr' or 'en'. Translations are added only for 'en'; Serbian text comes from the revision."),
        LocalizationErrorCodes.InvalidTitle => BadRequest(code,
            $"Title is required, must not exceed {LabTranslation.MaxTitleLength} characters and must not contain control characters."),
        LocalizationErrorCodes.InvalidDescription => BadRequest(code,
            $"Description must not exceed {LabTranslation.MaxDescriptionLength} characters; only line breaks and tabs are allowed as control characters."),
        LocalizationErrorCodes.InvalidInstructions => BadRequest(code,
            $"Instructions must not exceed {LabTranslation.MaxInstructionsLength} characters; only line breaks and tabs are allowed as control characters."),
        LocalizationErrorCodes.InvalidRevisionId => BadRequest(code, "Revision ID is invalid."),
        LocalizationErrorCodes.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied"),
        LocalizationErrorCodes.NotFound => NotFound(),
        LocalizationErrorCodes.Conflict => Results.Conflict(new ApiErrorResponse(
            code, "A translation in this language already exists for this revision.")),
        LocalizationErrorCodes.RevisionNotDraft => Results.Conflict(new ApiErrorResponse(
            code, "Translations can be added only while the revision is a draft.")),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error"),
    };

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new ApiErrorResponse(code, message));

    private static IResult NotFound() =>
        Results.NotFound(new ApiErrorResponse(LabErrorCodes.NotFound, "Laboratory content was not found."));

    private static IResult Unauthenticated() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthenticated");

    private static IResult InvalidBody() =>
        Results.BadRequest(new ApiErrorResponse(LabErrorCodes.InvalidBody, "Request body is invalid."));

    // Ručno čitanje tela: neispravan ili prevelik JSON uvek daje 400, i u Development režimu bez stack trace-a.
    private static async Task<(bool Ok, AddTranslationRequest? Value)> TryReadBodyAsync(HttpContext http)
    {
        HttpRequest request = http.Request;

        if (!request.HasJsonContentType() || request.ContentLength is > MaxBodyBytes)
        {
            return (false, null);
        }

        try
        {
            using var limited = new MemoryStream();
            byte[] buffer = new byte[4096];
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
            var value = await JsonSerializer.DeserializeAsync<AddTranslationRequest>(limited, JsonOptions, http.RequestAborted);
            return (value is not null, value);
        }
        catch (JsonException)
        {
            return (false, null);
        }
        catch (BadHttpRequestException)
        {
            // Kestrel prekida chunked telo veće od RequestSizeLimit pre naše provere dužine.
            return (false, null);
        }
    }

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
                .CreateLogger("LabLocalization.Api")
                .LogError(ex, "Unhandled error in lab localization endpoint, traceId={TraceId}", http.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error",
                extensions: new Dictionary<string, object?> { ["traceId"] = http.TraceIdentifier });
        }
    }
}
