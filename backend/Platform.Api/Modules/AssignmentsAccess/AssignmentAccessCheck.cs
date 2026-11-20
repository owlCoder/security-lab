using AccessEngine = Platform.Access.Contracts.IAccessDecision;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-04 / ASN-06 TIM 15
// One place that turns "may this account do this action on this assignment?" into a call to
// the platform access engine (Platform.Access, ACC-04 / ACC-05). The engine decides with
// deny by default, derives the course from the resource (never from a caller-supplied value),
// checks the active membership and the role, and writes the access audit record for every
// decision, including denials.
//
// Fail closed: a malformed ID, an unknown account, an engine error or a timeout of a dependency
// all end in "not allowed". Only a request cancelled by the caller (its own token) is passed on,
// so a caller can stop the work. An OperationCanceledException that the caller did not ask for
// (for example an internal timeout of the engine or of a reader) is a failure, not a
// cancellation, and is answered with "not allowed".
internal sealed class AssignmentAccessCheck(AccessEngine engine, IAccountStateReader accounts)
{
    public const string AssignmentResourceType = "assignment";

    public async ValueTask<bool> IsAllowedAsync(
        string accountId,
        string action,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        // The account ID is the logged-in user's ID resolved on the server (a GUID). Anything
        // else (empty, "teacher-1", a value copied from a request body) is not a known account.
        if (!Guid.TryParse(accountId, out var account) || account == Guid.Empty || assignmentId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var state = await accounts.GetStateAsync(account, cancellationToken).ConfigureAwait(false);
            if (state == AccountState.Unknown)
            {
                return false;
            }

            var request = new AccessRequest(
                new SubjectRef(account.ToString("D"), state),
                action,
                new ResourceRef(AssignmentResourceType, assignmentId.ToString("D")),
                Guid.NewGuid().ToString("N"));

            var decision = await engine.DecideAsync(request, cancellationToken).ConfigureAwait(false);
            return decision.Outcome == AccessOutcome.Allow;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
