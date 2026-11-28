using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Infrastructure;
using AccountState = Platform.Access.Domain.Enums.AccountState;

namespace Platform.Api.Controllers;

[RequireAccess(LabVisibilityActions.Entry)]
[ApiController]
[Route("api/laboratories/{laboratoryId:guid}/access")]
public sealed class LabAccessController(
    IPrerequisiteAccessService accessService,
    ILabPublicContentReader contentReader,
    ICourseMembership membership,
    IRequestSubjectResolver subjectResolver,
    IPrerequisiteRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid laboratoryId, CancellationToken cancellationToken)
    {
        var subject = subjectResolver.Resolve(HttpContext);

        if (subject is not { State: AccountState.Active } ||
            !Guid.TryParse(subject.Id, out var userId) ||
            userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var content = await contentReader.GetAsync(laboratoryId, cancellationToken);
        if (content is null)
            return NotFound();

        var role = await membership.GetRoleAsync(content.CourseId, userId, cancellationToken);

        if (role == CourseRole.Teacher)
            return Ok(new LabAccessResponse(true, Array.Empty<Guid>()));

        if (role != CourseRole.Student || content.State != LabVisibilityState.Published)
            return NotFound();

        try
        {
            var result = await accessService.CheckAccessAsync(userId, laboratoryId, cancellationToken);

            return Ok(new LabAccessResponse(result.IsUnlocked, result.MissingPrerequisites));
        }
        catch (InvalidOperationException)
            when (repository is UnavailablePrerequisiteRepository)
        {
            return StatusCode(503, new { code = "prerequisite_storage_unavailable" });
        }
    }

    public sealed record LabAccessResponse(bool IsUnlocked, IReadOnlyList<Guid> MissingPrerequisites);
}