namespace Team21.Acc.Core.Groups.Visibility;

/// <summary>An account's role in a course.</summary>
public enum CourseRole
{
    Student = 1,
    Assistant = 2,
    Teacher = 3
}

/// <summary>
/// An account's place in a course. CanReview is the review right a teacher delegated to an assistant.
/// </summary>
public sealed record CourseMembership(Guid CourseId, CourseRole Role, bool Active, bool CanReview);
