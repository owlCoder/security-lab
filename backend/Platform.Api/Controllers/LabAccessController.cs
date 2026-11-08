using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.Prerequisites.Contracts;
using AccountState = Platform.Access.Domain.Enums.AccountState;

namespace Platform.Api.Controllers;

[ApiController]
[DelegatedAuthorization]
[Route("api/laboratories/{laboratoryId:guid}/access")]
public sealed class LabAccessController(
    IPrerequisiteAccessService accessService,
    ILabPublicContentReader contentReader,
    ICourseMembership membership,
    IRequestSubjectResolver subjectResolver) : ControllerBase
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

        var result = await accessService.CheckAccessAsync(userId, laboratoryId, cancellationToken);

        return Ok(new LabAccessResponse(result.IsUnlocked, result.MissingPrerequisites));
    }

    public sealed record LabAccessResponse(bool IsUnlocked, IReadOnlyList<Guid> MissingPrerequisites);
}