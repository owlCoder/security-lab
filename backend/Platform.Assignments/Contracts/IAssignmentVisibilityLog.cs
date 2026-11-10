// ASN-05 TIM 13
namespace Platform.Assignments.Contracts;

// Records why an assignment was hidden from a student's list.
// Only the reason code is recorded, on the server side; the client never sees it.
// The adapter writes to the server log. Recording never makes an assignment visible.
public interface IAssignmentVisibilityLog
{
    ValueTask RecordHiddenAsync(
        string studentId,
        Guid assignmentId,
        string reasonCode,
        CancellationToken cancellationToken = default);
}
