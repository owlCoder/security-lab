
namespace Team21.Acc.Core.Courses;

/// <summary>
/// Defines permissions for course operations.
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
