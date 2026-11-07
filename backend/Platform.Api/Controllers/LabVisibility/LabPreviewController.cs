using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Enums;
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
    IIdentityContext identity,
    IAccountRepository accounts) : ControllerBase
{
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        // Nacrt ne sme da ostane u kešu browsera ili proxy-ja, ni kad je odgovor 401/404.
        Response.Headers.CacheControl = "no-store";

        var result = await preview.ExecuteAsync(
            id,
            CurrentSubject(),
            HttpContext.TraceIdentifier,
            cancellationToken);

        return result.Outcome switch
        {
            PreviewOutcome.Ok => Ok(result.Preview),
            PreviewOutcome.Unauthenticated => StatusCode(401, new { code = "unauthenticated" }),
            _ => NotFound(new { code = "lab_not_available" })
        };
    }

    // TODO(AUT-04): zameniti Stojanovićevim LabCaller helperom kad bude u main.
    // Identitet uvek iz IIdentityContext, nikad iz tela zahteva.
    private SubjectRef? CurrentSubject()
    {
        var userId = identity.Current?.UserId;
        if (userId is null) return null;

        var account = accounts.Find(userId.Value);
        if (account is null) return null;

        var state = account.Status switch
        {
            AccountStatus.Active => AccountState.Active,
            AccountStatus.Blocked => AccountState.Blocked,
            _ => AccountState.Archived
        };

        return new SubjectRef(userId.Value.ToString(), state);
    }
}
