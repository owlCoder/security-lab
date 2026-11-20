using Platform.Access.Domain.Constants;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-04 TIM 15
// Implements IStartAttemptAuthorization on top of the platform access engine.
// Only an active student member of the assignment's course may start an attempt: the engine
// action "attempt.create" is allowed for the Student role only. A teacher, a student of another
// course, a former member, a blocked account or an unknown assignment is denied.
public sealed class AccessStartAttemptAuthorization : IStartAttemptAuthorization
{
    private readonly AssignmentAccessCheck check;

    public AccessStartAttemptAuthorization(
        Platform.Access.Contracts.IAccessDecision engine,
        IAccountStateReader accounts)
    {
        check = new AssignmentAccessCheck(engine, accounts);
    }

    public ValueTask<bool> CanStartAsync(
        string studentId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default) =>
        check.IsAllowedAsync(studentId, AccessActions.AttemptCreate.Id, assignmentId, cancellationToken);
}
