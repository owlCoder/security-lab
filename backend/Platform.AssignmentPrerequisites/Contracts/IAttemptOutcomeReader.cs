namespace Platform.AssignmentPrerequisites.Contracts;

public interface IAttemptOutcomeReader
{
    ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
        string studentId,
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken = default);
}
