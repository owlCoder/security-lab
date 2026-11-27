namespace Team21.Acc.Core.Courses;

/// <summary>
/// Who may create, archive and read courses.
/// </summary>
public interface ICourseAuthorization
{
    Task<bool> CanCreateAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> CanArchiveAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<bool> CanViewAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default);
}
