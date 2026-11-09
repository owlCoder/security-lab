
using Team21.Acc.Core.Courses;

namespace Team21.Acc.Infrastructure.Courses.Authorization;

/// <summary>
/// Denies course operations until real authorization is integrated.
/// </summary>
public sealed class DenyAllCourseAuthorization : ICourseAuthorization
{
    public Task<bool> CanCreateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> CanArchiveAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> CanViewAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
