namespace Team21.Acc.Core.Groups.Visibility.Interfaces;

/// <summary>Reads whether a course is active or archived, null when it doesn't exist. Owned by ACC-01.</summary>
public interface ICourseStateReader
{
    Task<CourseState?> GetStateAsync(Guid courseId, CancellationToken ct = default);
}
