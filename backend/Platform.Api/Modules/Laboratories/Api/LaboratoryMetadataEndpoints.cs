
using System.Text.Json;
using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabMetadata;

namespace Platform.Api.Modules.Laboratories.Api;

public static class LaboratoryMetadataEndpoints
{
    public const string BasePath = "/api/laboratories";
    public const int MaxBodyBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLaboratoryMetadataEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Do not expose handlers with an unresolved storage dependency.
        // The in-memory adapter is Development-only; other environments need
        // an explicitly registered adapter before these routes are available.
        if (!app.ServiceProvider.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(ILaboratoryMetadataRepository)))
            return app;

        app.MapPost(
            $"{BasePath}/{{id:guid}}/metadata",
            async (
                Guid id,
                HttpContext httpContext,
                ILaboratoryRepository laboratoryRepository,
                ILaboratoryMetadataRepository metadataRepository,
                CancellationToken cancellationToken) =>
            {
                // Provera postojanja laboratorije
                var laboratory = await laboratoryRepository.GetByIdAsync(
                    id,
                    cancellationToken);

                if (laboratory is null)
                    return Results.NotFound();

                var (request, error) = await ReadRequestAsync(httpContext, cancellationToken);
                if (error is not null)
                    return error;
                // Validacija metapodataka
                var validator = new LaboratoryMetadataValidator();

                var validation = validator.Validate(
                    id,
                    request!.Objective,
                    request.Difficulty,
                    request.Type,
                    request.Tags);

                if (!validation.IsValid)
                    return Results.ValidationProblem(validation.Errors);

                // Kreiranje mora biti atomsko i pri paralelnim POST zahtevima.
                var metadata = new LaboratoryMetadata(
                    id,
                    request.Objective!,
                    request.Difficulty,
                    request.Type,
                    request.Tags!);

                if (!await metadataRepository.TryAddAsync(metadata, cancellationToken))
                    return Results.Conflict(new
                    {
                        error = "Laboratory metadata already exists."
                    });

                // Uspesan odgovor
                var response = new LaboratoryMetadataResponse(
                    metadata.LaboratoryId,
                    metadata.Objective,
                    metadata.Difficulty,
                    metadata.Type,
                    metadata.Tags);

                return Results.Created(
                    $"{BasePath}/{id}/metadata",
                    response);
            })
            .RequireAccess(AccessActions.LabDraftManage.Id, "laboratory", "id");


        app.MapPatch(
            $"{BasePath}/{{id:guid}}/metadata",
            async (
                Guid id,
                HttpContext httpContext,
                ILaboratoryRepository laboratoryRepository,
                ILaboratoryMetadataRepository metadataRepository,
                CancellationToken cancellationToken) =>
            {
                var laboratory = await laboratoryRepository.GetByIdAsync(
                    id,
                    cancellationToken);

                if (laboratory is null)
                    return Results.NotFound();

                var existing = await metadataRepository
                    .GetByLaboratoryIdAsync(id, cancellationToken);

                if (existing is null)
                    return Results.NotFound(new
                    {
                        error = "Laboratory metadata was not found."
                    });

                var (request, error) = await ReadRequestAsync(httpContext, cancellationToken);
                if (error is not null)
                    return error;
                var validator = new LaboratoryMetadataValidator();

                var validation = validator.Validate(
                    id,
                    request!.Objective,
                    request.Difficulty,
                    request.Type,
                    request.Tags);

                if (!validation.IsValid)
                    return Results.ValidationProblem(validation.Errors);

                var updated = new LaboratoryMetadata(
                    id,
                    request.Objective!,
                    request.Difficulty,
                    request.Type,
                    request.Tags!);

                await metadataRepository.SaveAsync(
                    updated,
                    cancellationToken);

                return Results.Ok(new LaboratoryMetadataResponse(
                    updated.LaboratoryId,
                    updated.Objective,
                    updated.Difficulty,
                    updated.Type,
                    updated.Tags));
            })
            .RequireAccess(AccessActions.LabDraftManage.Id, "laboratory", "id");


        return app;
    }
    // Bound bytes before deserializing, including requests without Content-Length.
    private static async Task<(LaboratoryMetadataRequest? Request, IResult? Error)> ReadRequestAsync(
        HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
            return (null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));

        if (!context.Request.HasJsonContentType())
            return (null, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));

        var buffer = new byte[MaxBodyBytes + 1];
        var length = await context.Request.Body.ReadAtLeastAsync(
            buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken: cancellationToken);
        if (length > MaxBodyBytes)
            return (null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));

        try
        {
            var request = JsonSerializer.Deserialize<LaboratoryMetadataRequest>(
                buffer.AsSpan(0, length), JsonOptions);
            return request is null
                ? (null, Results.BadRequest(new { error = "Request body is required." }))
                : (request, null);
        }
        catch (JsonException)
        {
            return (null, Results.BadRequest(new { error = "Invalid JSON request." }));
        }
    }
}
