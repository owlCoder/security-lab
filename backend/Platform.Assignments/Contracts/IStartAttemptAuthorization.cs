namespace Platform.Assignments.Contracts;

public interface IStartAttemptAuthorization
{
    ValueTask<bool> CanStartAsync(
        string studentId,
        Guid assignmentId,
        string courseId,
        CancellationToken cancellationToken = default);
}