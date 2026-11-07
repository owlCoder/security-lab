using Microsoft.AspNetCore.Mvc;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;
using Platform.Access.AspNet;

namespace Platform.Api.Controllers;

[ApiController]
[DelegatedAuthorization]
[Route("api/laboratories/{laboratoryId:guid}/prerequisites")]
public sealed class PrerequisitesController(
    IPrerequisiteRepository repository,
    Platform.Access.Contracts.IAccessDecision accessDecision,
    IRequestSubjectResolver subjectResolver) : ControllerBase
{
    [HttpGet]
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

        var prerequisites = await repository.GetForLaboratoryAsync(
            laboratoryId, cancellationToken);

        return Ok(prerequisites.Select(p => p.RequiredLaboratoryId));
    }

    [HttpPost]
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

        var result = await repository.AddAsync(
            prerequisite, cancellationToken);

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
            return BadRequest(new { code = "invalid_prerequisite" });
        }
    }

    public sealed record AddPrerequisiteRequest(Guid RequiredLaboratoryId);
}