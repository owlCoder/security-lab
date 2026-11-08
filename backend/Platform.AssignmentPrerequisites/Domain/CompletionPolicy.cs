using Platform.AssignmentPrerequisites.Contracts;

namespace Platform.AssignmentPrerequisites.Domain;

public static class CompletionPolicy
{
    public static bool CountsAsDone(
        AttemptOutcomeSnapshot attempt,
        CompletionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.AttemptState != AttemptState.Completed)
        {
            return false;
        }

        if (attempt.EvaluationStatus != EvaluationStatus.Completed)
        {
            return false;
        }

        return attempt.EvaluationOutcome switch
        {
            EvaluationOutcome.Passed => true,
            EvaluationOutcome.Partial => requirement == CompletionRequirement.PassedOrPartial,
            _ => false
        };
    }

    public static bool IsSatisfied(
        string studentId,
        AssignmentPrerequisite prerequisite,
        IEnumerable<AttemptOutcomeSnapshot> attempts)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);
        ArgumentNullException.ThrowIfNull(attempts);

        if (!Identifiers.IsValidPrincipalId(studentId))
        {
            throw new ArgumentException("Student ID is invalid.", nameof(studentId));
        }

        return attempts.Any(attempt =>
            string.Equals(attempt.StudentId, studentId, StringComparison.Ordinal) &&
            attempt.AssignmentId == prerequisite.RequiredAssignmentId &&
            CountsAsDone(attempt, prerequisite.CompletionRequirement));
    }

    public static IReadOnlyList<Guid> FindMissing(
        string studentId,
        IEnumerable<AssignmentPrerequisite> prerequisites,
        IReadOnlyCollection<AttemptOutcomeSnapshot> attempts)
    {
        ArgumentNullException.ThrowIfNull(prerequisites);
        ArgumentNullException.ThrowIfNull(attempts);

        if (!Identifiers.IsValidPrincipalId(studentId))
        {
            throw new ArgumentException("Student ID is invalid.", nameof(studentId));
        }

        return prerequisites
            .Where(prerequisite => !IsSatisfied(studentId, prerequisite, attempts))
            .Select(prerequisite => prerequisite.RequiredAssignmentId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
