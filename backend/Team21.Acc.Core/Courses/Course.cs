using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Courses;

public class Course : BaseEntity
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 4000;

    // Used by EF when loading an existing course.
    private Course() { }

    public Course(string code, string name, string? description, Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        code = code.Trim().ToUpperInvariant();
        if (code.Length > MaxCodeLength)
            throw new ArgumentException($"Course code must not exceed {MaxCodeLength} characters.", nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        description = description?.Trim() ?? string.Empty;

        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Course name must not exceed {MaxNameLength} characters.", nameof(name));
        if (description.Length > MaxDescriptionLength)
            throw new ArgumentException($"Course description must not exceed {MaxDescriptionLength} characters.", nameof(description));
        if (createdBy == Guid.Empty)
            throw new ArgumentException("A course creator is required.", nameof(createdBy));

        Code = code;
        Name = name;
        Description = description;
        CreatedBy = createdBy;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public CourseStatus Status { get; private set; } = CourseStatus.Active;
    public DateTime? ArchivedAt { get; private set; }
    public Guid? ArchivedBy { get; private set; }

    /// <summary>
    /// Enrollment services must check this against the current persisted course before adding a member.
    /// Archiving does not revoke or remove existing memberships or results.
    /// </summary>
    public bool CanEnroll => Status == CourseStatus.Active && !IsDeleted;

    /// <summary>Repeated archiving preserves the original archive actor and time.</summary>
    public void Archive(Guid archivedBy)
    {
        if (archivedBy == Guid.Empty)
            throw new ArgumentException("An archive actor is required.", nameof(archivedBy));
        if (IsDeleted)
            throw new InvalidOperationException("A deleted course cannot be archived.");
        if (Status == CourseStatus.Archived)
            return;

        Status = CourseStatus.Archived;
        ArchivedAt = DateTime.UtcNow;
        ArchivedBy = archivedBy;
        UpdatedBy = archivedBy;
    }
}
