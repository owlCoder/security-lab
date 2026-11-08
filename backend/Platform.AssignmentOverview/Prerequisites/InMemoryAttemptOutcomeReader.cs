using Platform.AssignmentPrerequisites.Contracts;
 
namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Privremena zamena za modul pokusaja/evaluacije (ATT/EVAL), koji jos ne postoji.
public sealed class InMemoryAttemptOutcomeReader : IAttemptOutcomeReader
{
    private readonly List<AttemptOutcomeSnapshot> _attempts = [];
 
    public void Add(AttemptOutcomeSnapshot attempt) => _attempts.Add(attempt);
 
    // Skraceno: student je zavrsio zadatak sa ishodom "uspesan".
    public void AddPassed(string studentId, Guid assignmentId) =>
        Add(new AttemptOutcomeSnapshot(
            Guid.NewGuid(),
            assignmentId,
            studentId,
            AttemptState.Completed,
            EvaluationStatus.Completed,
            EvaluationOutcome.Passed));
 
    public ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
        string studentId,
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AttemptOutcomeSnapshot> result = _attempts
            .Where(a => a.StudentId == studentId && assignmentIds.Contains(a.AssignmentId))
            .ToList();
 
        return ValueTask.FromResult(result);
    }
}