namespace Platform.AssignmentPrerequisites.Domain;

public sealed class AssignmentPrerequisite
{
    public AssignmentPrerequisite(
        string courseId,
        Guid assignmentId,
        Guid requiredAssignmentId,
        CompletionRequirement completionRequirement,
        string createdById,
        DateTimeOffset createdAtUtc)
    {
        if (!Identifiers.IsValidCourseId(courseId))
        {
            throw new ArgumentException("Course ID is invalid.", nameof(courseId));
        }

        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException("Assignment ID is required.", nameof(assignmentId));
        }

        if (requiredAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Required assignment ID is required.", nameof(requiredAssignmentId));
        }

        if (assignmentId == requiredAssignmentId)
        {
            throw new ArgumentException(
                "An assignment cannot be its own prerequisite.",
                nameof(requiredAssignmentId));
        }

        if (!Enum.IsDefined(completionRequirement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completionRequirement),
                "Unknown completion requirement.");
        }

        if (!Identifiers.IsValidPrincipalId(createdById))
        {
            throw new ArgumentException("Creator ID is invalid.", nameof(createdById));
        }

        CourseId = courseId;
        AssignmentId = assignmentId;
        RequiredAssignmentId = requiredAssignmentId;
        CompletionRequirement = completionRequirement;
        CreatedById = createdById;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public string CourseId { get; }

    public Guid AssignmentId { get; }

    public Guid RequiredAssignmentId { get; }

    public CompletionRequirement CompletionRequirement { get; }

    public string CreatedById { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public bool IsSameRelation(AssignmentPrerequisite other) =>
        AssignmentId == other.AssignmentId &&
        RequiredAssignmentId == other.RequiredAssignmentId;
}
