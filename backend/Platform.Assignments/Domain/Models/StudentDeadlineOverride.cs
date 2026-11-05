using Platform.Assignments.Domain.ValueObjects;

namespace Platform.Assignments.Domain.Models;

// ASN-06 TIM 15
// Individual deadline of one student for one assignment. It can only extend the deadline.
// It keeps the previous deadline and who changed it (ASN-19), and it is never edited in place:
// a further extension creates a new value that points back to the deadline it replaced.
public sealed class StudentDeadlineOverride
{
    public StudentDeadlineOverride(
        Guid assignmentId,
        string studentId,
        DateTimeOffset dueAtUtc,
        DateTimeOffset previousDueAtUtc,
        string extendedById,
        DateTimeOffset extendedAtUtc)
    {
        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException("Assignment ID is required.", nameof(assignmentId));
        }

        if (!PrincipalId.IsValid(studentId))
        {
            throw new ArgumentException("Student ID is invalid.", nameof(studentId));
        }

        if (!PrincipalId.IsValid(extendedById))
        {
            throw new ArgumentException("Extending user ID is invalid.", nameof(extendedById));
        }

        if (dueAtUtc <= previousDueAtUtc)
        {
            throw new ArgumentException(
                "The individual deadline must be later than the previous deadline.",
                nameof(dueAtUtc));
        }

        AssignmentId = assignmentId;
        StudentId = studentId;
        DueAtUtc = dueAtUtc.ToUniversalTime();
        PreviousDueAtUtc = previousDueAtUtc.ToUniversalTime();
        ExtendedById = extendedById;
        ExtendedAtUtc = extendedAtUtc.ToUniversalTime();
    }

    public Guid AssignmentId { get; }

    public string StudentId { get; }

    public DateTimeOffset DueAtUtc { get; }

    public DateTimeOffset PreviousDueAtUtc { get; }

    public string ExtendedById { get; }

    public DateTimeOffset ExtendedAtUtc { get; }
}
