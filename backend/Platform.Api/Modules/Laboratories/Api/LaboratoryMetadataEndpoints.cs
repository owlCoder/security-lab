
using System.Text.Json;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabMetadata;

namespace Platform.Api.Modules.Laboratories.Api;

public static class LaboratoryMetadataEndpoints
{
    public const string BasePath = "/api/laboratories";

    public static IEndpointRouteBuilder MapLaboratoryMetadataEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            $"{BasePath}/{{id:guid}}/metadata",
            async (
                Guid id,
                HttpContext httpContext,
                IAccessSubjectProvider subjectProvider,
                Platform.Access.Contracts.IAccessDecision accessDecision,
                ILaboratoryRepository laboratoryRepository,
                ILaboratoryMetadataRepository metadataRepository,
                CancellationToken cancellationToken) =>
            {
                // 1. Identitet korisnika
                var subject = await subjectProvider.GetSubjectAsync(
                    httpContext,
                    cancellationToken);

                if (subject is null)
                    return Results.Unauthorized();

                // 2. ACC provera prava
                var resource = new ResourceRef(
                    "laboratory",
                    id.ToString());

                var decision = await accessDecision.DecideAsync(
                    new AccessRequest(
                        subject,
                        AccessActions.LabDraftManage.Id,
                        resource,
                        httpContext.TraceIdentifier),
                    cancellationToken);

                if (decision.Outcome != AccessOutcome.Allow)
                    return Results.StatusCode(
                        StatusCodes.Status403Forbidden);

                // 3. Provera postojanja laboratorije
                var laboratory = await laboratoryRepository.GetByIdAsync(
                    id,
                    cancellationToken);

                if (laboratory is null)
                    return Results.NotFound();

                // 4. Citanje zahteva
                LaboratoryMetadataRequest? request;

                try
                {
                    request = await httpContext.Request.ReadFromJsonAsync<
                        LaboratoryMetadataRequest>(
                        cancellationToken);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid JSON request."
                    });
                }

                if (request is null)
                    return Results.BadRequest(new
                    {
                        error = "Request body is required."
                    });

                // 5. Validacija metapodataka
                var validator = new LaboratoryMetadataValidator();

                var validation = validator.Validate(
                    id,
                    request.Objective,
                    request.Difficulty,
                    request.Type,
                    request.Tags);

                if (!validation.IsValid)
                    return Results.ValidationProblem(validation.Errors);

                // 6. Provera da metapodaci vec ne postoje
                var existing = await metadataRepository
                    .GetByLaboratoryIdAsync(id, cancellationToken);

                if (existing is not null)
                    return Results.Conflict(new
                    {
                        error = "Laboratory metadata already exists."
                    });

                // 7. Kreiranje i cuvanje metapodataka
                var metadata = new LaboratoryMetadata(
                    id,
                    request.Objective!,
                    request.Difficulty,
                    request.Type,
                    request.Tags!);

                await metadataRepository.SaveAsync(
                    metadata,
                    cancellationToken);

                // 8. Uspesan odgovor
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
            .DelegatesToLegacyAuthorization();


        app.MapPatch(
            $"{BasePath}/{{id:guid}}/metadata",
            async (
                Guid id,
                HttpContext httpContext,
                IAccessSubjectProvider subjectProvider,
                Platform.Access.Contracts.IAccessDecision accessDecision,
                ILaboratoryRepository laboratoryRepository,
                ILaboratoryMetadataRepository metadataRepository,
                CancellationToken cancellationToken) =>
            {
                var subject = await subjectProvider.GetSubjectAsync(
                    httpContext,
                    cancellationToken);

                if (subject is null)
                    return Results.Unauthorized();

                var resource = new ResourceRef(
                    "laboratory",
                    id.ToString());

                var decision = await accessDecision.DecideAsync(
                    new AccessRequest(
                        subject,
                        AccessActions.LabDraftManage.Id,
                        resource,
                        httpContext.TraceIdentifier),
                    cancellationToken);

                if (decision.Outcome != AccessOutcome.Allow)
                    return Results.StatusCode(
                        StatusCodes.Status403Forbidden);

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

                LaboratoryMetadataRequest? request;

                try
                {
                    request = await httpContext.Request
                        .ReadFromJsonAsync<LaboratoryMetadataRequest>(
                            cancellationToken);
                }
                catch (System.Text.Json.JsonException)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid JSON request."
                    });
                }

                if (request is null)
                    return Results.BadRequest(new
                    {
                        error = "Request body is required."
                    });

                var validator = new LaboratoryMetadataValidator();

                var validation = validator.Validate(
                    id,
                    request.Objective,
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
            .DelegatesToLegacyAuthorization();


        return app;
    }
}
