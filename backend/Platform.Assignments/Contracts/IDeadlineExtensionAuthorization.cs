namespace Platform.Assignments.Contracts;

// ASN-06 TIM 15
// Server-side decision: may this user extend deadlines of this assignment's course?
// The implementation (Access / membership module) must deny by default (ACC-05).
// See docs/contracts/ASN-06-contracts.md.
public interface IDeadlineExtensionAuthorization
{
    ValueTask<bool> CanExtendAsync(
        string actorId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default);
}
