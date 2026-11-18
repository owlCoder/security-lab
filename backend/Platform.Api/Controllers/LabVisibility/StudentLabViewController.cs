using Microsoft.AspNetCore.Mvc;
using Platform.Api.Services.Identity;
using Platform.LabVisibility.Application.StudentView;
using Platform.Access.AspNet;

namespace Platform.Api.Controllers.LabVisibility;

[ApiController]
[Route("api/labs")]
[DelegatedAuthorization]
public sealed class StudentLabViewController(LabCaller labCaller, GetStudentLabView getStudentLabView) : ControllerBase
{
    [HttpGet("{id:guid}/student-view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var subject = await labCaller.GetSubjectAsync(cancellationToken);

        if (subject is null)
        {
            return Unauthorized();
        }

        var view = await getStudentLabView.ExecuteAsync(id, subject, HttpContext.TraceIdentifier, cancellationToken);

        if (view is null)
        {
            return NotFound();
        }

        return Ok(view);
    }
}