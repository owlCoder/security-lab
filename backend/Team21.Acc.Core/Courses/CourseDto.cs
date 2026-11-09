namespace Team21.Acc.Core.Courses;

/// <summary>A course as the api returns it.</summary>
public sealed record CourseDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Status,
    DateTime CreatedAt,
    DateTime? ArchivedAt
);