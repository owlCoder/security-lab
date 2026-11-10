using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Infrastructure;

// ASN-07: privremeni podaci za prikaz „zaključano“.
// Koriste se samo u Developmentu, dok modul za pokušaje (ATT) nema implementaciju.
public static class DevelopmentLockData
{
    // Zaključana: zahteva OIB vežbu 1, a student je još nije položio.
    public static readonly Guid LockedAssignmentId = Guid.Parse("a0000000-0000-0000-0000-000000000004");

    // Otključana: zahteva Security vežbu 1, a student ju je položio.
    public static readonly Guid UnlockedAssignmentId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

    // Isti ID kao nalog teacher@example.com iz razvojnog seed-a identiteta.
    private const string TeacherId = "44444444-4444-4444-4444-444444444444";

    private static readonly DateTimeOffset Due = new(2027, 1, 31, 22, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<Assignment> CreateAssignments() =>
    [
        new Assignment(LockedAssignmentId, DevelopmentAssignmentData.CourseOib, "rev-sql-injection-2", null, Due, 3),
        new Assignment(UnlockedAssignmentId, DevelopmentAssignmentData.CourseSecurity, "rev-xss-2", null, Due, 2)
    ];

    public static IReadOnlyList<AssignmentPrerequisite> CreatePrerequisites() =>
    [
        new AssignmentPrerequisite(
            DevelopmentAssignmentData.CourseOib,
            LockedAssignmentId,
            DevelopmentAssignmentData.OibAssignmentId,
            CompletionRequirement.Passed,
            TeacherId,
            CreatedAt),
        new AssignmentPrerequisite(
            DevelopmentAssignmentData.CourseSecurity,
            UnlockedAssignmentId,
            DevelopmentAssignmentData.SecurityAssignmentId,
            CompletionRequirement.Passed,
            TeacherId,
            CreatedAt)
    ];

    public static IReadOnlyList<AttemptOutcomeSnapshot> CreateAttempts() =>
    [
        // Security vežbu 1 je student položio, pa je Security vežba 2 otključana.
        new AttemptOutcomeSnapshot(
            Guid.Parse("b0000000-0000-0000-0000-000000000001"),
            DevelopmentAssignmentData.SecurityAssignmentId,
            DevelopmentAssignmentData.StudentId,
            AttemptState.Completed,
            EvaluationStatus.Completed,
            EvaluationOutcome.Passed),

        // OIB vežbu 1 je predao, ali ocenjivanje još traje, pa se ne računa kao urađena.
        new AttemptOutcomeSnapshot(
            Guid.Parse("b0000000-0000-0000-0000-000000000002"),
            DevelopmentAssignmentData.OibAssignmentId,
            DevelopmentAssignmentData.StudentId,
            AttemptState.InEvaluation,
            EvaluationStatus.Running,
            null)
    ];

    public static IAssignmentPrerequisiteStore CreatePrerequisiteStore()
    {
        var store = new InMemoryAssignmentPrerequisiteStore();

        foreach (AssignmentPrerequisite prerequisite in CreatePrerequisites())
        {
            // Skladište je u memoriji i završava odmah, pa je sinhrono čekanje ovde bezbedno.
            store.AddAsync(prerequisite).AsTask().GetAwaiter().GetResult();
        }

        return store;
    }
}

// ASN-07: privremeni izvor pokušaja za Development.
// Vraća samo pokušaje traženog studenta za tražene dodele.
public sealed class DevelopmentAttemptOutcomeReader : IAttemptOutcomeReader
{
    private readonly IReadOnlyList<AttemptOutcomeSnapshot> _attempts = DevelopmentLockData.CreateAttempts();

    public ValueTask<IReadOnlyList<AttemptOutcomeSnapshot>> GetForStudentAsync(
        string studentId,
        IReadOnlyCollection<Guid> assignmentIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AttemptOutcomeSnapshot> result = _attempts
            .Where(a => string.Equals(a.StudentId, studentId, StringComparison.Ordinal))
            .Where(a => assignmentIds.Contains(a.AssignmentId))
            .ToList();

        return ValueTask.FromResult(result);
    }
}