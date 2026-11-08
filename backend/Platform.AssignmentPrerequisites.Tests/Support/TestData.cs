using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentPrerequisites.Tests.Support;

internal static class TestData
{
    public const string Course = "course-oib";
    public const string OtherCourse = "course-other";
    public const string Teacher = "33333333-3333-3333-3333-333333333333";
    public const string Student = "22222222-2222-2222-2222-222222222222";
    public const string OtherStudent = "44444444-4444-4444-4444-444444444444";

    public static readonly Guid AssignmentA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    public static readonly Guid AssignmentB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    public static readonly Guid AssignmentC = Guid.Parse("b0000000-0000-0000-0000-00000000000c");
    public static readonly Guid AssignmentOtherCourse = Guid.Parse("b0000000-0000-0000-0000-0000000000ff");

    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public static AssignmentPrerequisite Requires(
        Guid assignment,
        Guid required,
        CompletionRequirement requirement = CompletionRequirement.Passed,
        string course = Course) =>
        new(course, assignment, required, requirement, Teacher, Now);

    public static AttemptOutcomeSnapshot Attempt(
        Guid assignment,
        AttemptState state = AttemptState.Completed,
        EvaluationStatus? evaluationStatus = EvaluationStatus.Completed,
        EvaluationOutcome? outcome = EvaluationOutcome.Passed,
        string student = Student) =>
        new(Guid.NewGuid(), assignment, student, state, evaluationStatus, outcome);

    public static Assignment AssignmentIn(Guid id, string course) =>
        new(id, course, "rev-test-1", null, Now.AddDays(30), 3);
}

internal sealed class FakeAssignmentRepository : IAssignmentRepository
{
    private readonly Dictionary<Guid, Assignment> _assignments = new();

    public FakeAssignmentRepository(params Assignment[] assignments)
    {
        foreach (Assignment assignment in assignments)
        {
            _assignments[assignment.Id] = assignment;
        }
    }

    public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_assignments.GetValueOrDefault(assignmentId));

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Assignment> result = _assignments.Values
            .Where(a => courseIds.Contains(a.CourseId))
            .ToList();

        return ValueTask.FromResult(result);
    }

    public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        _assignments[assignment.Id] = assignment;
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        _assignments[assignment.Id] = assignment;
        return ValueTask.CompletedTask;
    }
}
