using Platform.Access.AspNet;
 using Microsoft.AspNetCore.Mvc;
 using Platform.Api.Enums.Identity;
 using Platform.Api.Models.Identity;
using Platform.Api.Security.IdentityAccess;
 using Platform.Api.Services.Identity;

 namespace Platform.Api.Controllers.Identity;

 [ApiController]
 [Route("api/admin/accounts")]
 public class AdminAccountsController(ChangeAccountStatus changeStatus) : ControllerBase
 {
     [RequireAccess(IdentityAccessActions.AccountStatusChange)]
     [HttpPut("{id:guid}/status")]
     public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeStatusRequest body, CancellationToken ct)
     {
         var result = await changeStatus.ExecuteAsync(id, body.Status, ct);

         return result.Outcome switch
         {
             ChangeStatusOutcome.Success => Ok(new { id, status = result.NewStatus!.Value.ToString().ToUpperInvariant() }),
             ChangeStatusOutcome.Unauthenticated => StatusCode(401, new { code = "unauthenticated" }),
             ChangeStatusOutcome.Forbidden => StatusCode(403, new { code = "forbidden" }),
             ChangeStatusOutcome.NotFound => NotFound(new { code = "account_not_found" }),
            ChangeStatusOutcome.InvalidStatus => BadRequest(new { code = "invalid_status" }),
             ChangeStatusOutcome.InvalidTransition => Conflict(new { code = "invalid_transition" }),
             ChangeStatusOutcome.SelfChangeNotAllowed => Conflict(new { code = "self_change_not_allowed" }),
             ChangeStatusOutcome.Unavailable => StatusCode(503, new { code = "unavailable" }),
             _ => StatusCode(500)
         };
     }
}