using Platform.Access.Domain.Constants;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-06 TIM 15
// Implements IDeadlineExtensionAuthorization on top of the platform access engine.
// Only an active teacher of the assignment's course may extend a deadline: the engine action
// "assignment.manage" is allowed for the Teacher role only and an assistant cannot receive it
// by delegation. A student, a teacher of another course, a blocked account or an unknown
// assignment is denied. The courseId argument is not trusted: the course is derived by the
// access engine from the assignment resource.
public sealed class AccessDeadlineExtensionAuthorization : IDeadlineExtensionAuthorization
{
    private readonly AssignmentAccessCheck check;

    public AccessDeadlineExtensionAuthorization(
        Platform.Access.Contracts.IAccessDecision engine,
        IAccountStateReader accounts)
    {
        check = new AssignmentAccessCheck(engine, accounts);
    }

    public ValueTask<bool> CanExtendAsync(
        string actorId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default) =>
        check.IsAllowedAsync(actorId, AccessActions.AssignmentManage.Id, assignmentId, cancellationToken);
}
