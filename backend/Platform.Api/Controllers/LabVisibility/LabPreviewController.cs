using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Services.Identity;
using Platform.LabVisibility.Application.Preview;

namespace Platform.Api.Controllers.LabVisibility;

// AUT-07: "Pregled kao student" za nastavnika kursa.
// 200 dozvoljeno, 401 bez identiteta, 404 i za zabranjeno i za nepostojeće (isto telo).
// ACC-05: autorizacija je u PreviewStudentView (IAccessDecision + LabPreviewPolicy) pre čitanja
// sadržaja; middleware bi odbijen pristup vratio kao 403, a AUT-07 traži isti 404.
[DelegatedAuthorization]
[ApiController]
[Route("api/labs")]
public sealed class LabPreviewController(
    PreviewStudentView preview,
    LabCaller labCaller) : ControllerBase
{
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        // Nacrt ne sme da ostane u kešu browsera ili proxy-ja, ni kad je odgovor 401/404.
        Response.Headers.CacheControl = "no-store";

        // Identitet i status naloga iz sesije, isto kao za studentski prikaz (AUT-04, Stojanović).
        var subject = await labCaller.GetSubjectAsync(cancellationToken);

        var result = await preview.ExecuteAsync(
            id,
            subject,
            HttpContext.TraceIdentifier,
            cancellationToken);

        return result.Outcome switch
        {
            PreviewOutcome.Ok => Ok(result.Preview),
            PreviewOutcome.Unauthenticated => StatusCode(401, new { code = "unauthenticated" }),
            _ => NotFound(new { code = "lab_not_available" })
        };
    }
}
