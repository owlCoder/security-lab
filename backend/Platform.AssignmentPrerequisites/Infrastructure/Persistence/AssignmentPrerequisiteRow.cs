namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public sealed class AssignmentPrerequisiteRow
{
    public string CourseId { get; set; } = string.Empty;

    public Guid AssignmentId { get; set; }

    public Guid RequiredAssignmentId { get; set; }

    public short CompletionRequirement { get; set; }

    public string CreatedById { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
