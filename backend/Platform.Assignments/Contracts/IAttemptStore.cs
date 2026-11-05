namespace Platform.Assignments.Contracts;

public interface IAttemptStore
{
    ValueTask<Guid?> TryStartAsync(
        Guid assignmentId,
        string studentId,
        string publishedLabRevisionId,
        int maxAttempts,
        Guid requestId,
        CancellationToken cancellationToken = default);
}
