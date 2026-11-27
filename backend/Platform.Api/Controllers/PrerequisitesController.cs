using Microsoft.AspNetCore.Mvc;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;
using Platform.Access.AspNet;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/laboratories/{laboratoryId:guid}/prerequisites")]
public sealed class PrerequisitesController(
    IPrerequisiteRepository repository,
    Platform.Access.Contracts.IAccessDecision accessDecision,
    IRequestSubjectResolver subjectResolver,
    ILaboratoryRepository laboratories) : ControllerBase
{
    [HttpGet]
    [RequireAccess("prerequisites.read", ResourceType = "laboratory", IdRouteParam = "laboratoryId")]
    public async Task<IActionResult> Get(
    Guid laboratoryId,
    CancellationToken cancellationToken)
    {
        if (laboratoryId == Guid.Empty)
            return BadRequest(new { code = "invalid_laboratory_id" });

        var subject = subjectResolver.Resolve(HttpContext);

        if (subject is null)
        {
            return StatusCode(403, new { code = "forbidden" });
        }

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                subject,
                "prerequisites.read",
                new ResourceRef("laboratory", laboratoryId.ToString()),
                HttpContext.TraceIdentifier),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return StatusCode(403, new { code = "forbidden" });
        }


        try
        {
            var prerequisites = await repository.GetForLaboratoryAsync(
                laboratoryId, cancellationToken);

            return Ok(prerequisites.Select(p => p.RequiredLaboratoryId));
        }
        catch (InvalidOperationException)
            when (repository is UnavailablePrerequisiteRepository)
        {
            return StatusCode(503, new
            {
                code = "prerequisite_storage_unavailable"
            });
        }

    }

    [HttpPost]
    [RequireAccess("prerequisites.add", ResourceType = "laboratory", IdRouteParam = "laboratoryId")]
    public async Task<IActionResult> Add(
    Guid laboratoryId,
    [FromBody] AddPrerequisiteRequest request,
    CancellationToken cancellationToken)
    {
        var subject = subjectResolver.Resolve(HttpContext);

        if (subject is null)
        {
            return StatusCode(403, new { code = "forbidden" });
        }
        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                subject,
                "prerequisites.add",
                new ResourceRef("laboratory", laboratoryId.ToString()),
                HttpContext.TraceIdentifier),
        cancellationToken);
        if (decision.Outcome != AccessOutcome.Allow)
        {
            return StatusCode(403, new { code = "forbidden" });
        }
        LabPrerequisite prerequisite;

        try
        {
            prerequisite = new LabPrerequisite(
                laboratoryId,
                request.RequiredLaboratoryId);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { code = "invalid_prerequisite" });
        }

        var requiredLaboratory = await laboratories.GetByIdAsync(
            prerequisite.RequiredLaboratoryId,
            cancellationToken);

        if (requiredLaboratory is null)
        {
            return NotFound(new
            {
                code = "prerequisite_laboratory_not_found"
            });
        }

        AddPrerequisiteResult result;

        try
        {
            result = await repository.AddAsync(
                prerequisite, cancellationToken);
        }
        catch (InvalidOperationException)
            when (repository is UnavailablePrerequisiteRepository)
        {
            return StatusCode(503, new
            {
                code = "prerequisite_storage_unavailable"
            });
        }


        return result switch
        {
            AddPrerequisiteResult.Added =>
                StatusCode(201, new
                {
                    prerequisite.LaboratoryId,
                    prerequisite.RequiredLaboratoryId
                }),

            AddPrerequisiteResult.Duplicate =>
                Conflict(new { code = "duplicate_prerequisite" }),

            AddPrerequisiteResult.WouldCreateCycle =>
                Conflict(new { code = "prerequisite_cycle" }),

            _ => StatusCode(500)
        };
    }

    [HttpDelete("{requiredLaboratoryId:guid}")]
    [RequireAccess("prerequisites.remove", ResourceType = "laboratory", IdRouteParam = "laboratoryId")]
    public async Task<IActionResult> Remove(
    Guid laboratoryId,
    Guid requiredLaboratoryId,
    CancellationToken cancellationToken)
    {
        var subject = subjectResolver.Resolve(HttpContext);

        if (subject is null)
        {
            return StatusCode(403, new { code = "forbidden" });
        }

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                subject,
                "prerequisites.remove",
                new ResourceRef("laboratory", laboratoryId.ToString()),
                HttpContext.TraceIdentifier),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return StatusCode(403, new { code = "forbidden" });
        }

        try
        {
            var prerequisite = new LabPrerequisite(
                laboratoryId,
                requiredLaboratoryId);

            await repository.RemoveAsync(
                prerequisite,
                cancellationToken);

            return NoContent();
        }


        catch (ArgumentException)
        {
            return BadRequest(new
            {
                code = "invalid_prerequisite"
            });
        }


        catch (InvalidOperationException)
            when (repository is UnavailablePrerequisiteRepository)
        {
            return StatusCode(503, new
            {
                code = "prerequisite_storage_unavailable"
            });
        }

    }

    public sealed record AddPrerequisiteRequest(Guid RequiredLaboratoryId);
}