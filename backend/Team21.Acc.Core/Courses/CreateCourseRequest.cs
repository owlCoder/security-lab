namespace Team21.Acc.Core.Courses;

/// <summary>Body of POST /api/courses. Code is stored upper case.</summary>
public sealed record CreateCourseRequest(
    string? Code,
    string? Name,
    string? Description
);