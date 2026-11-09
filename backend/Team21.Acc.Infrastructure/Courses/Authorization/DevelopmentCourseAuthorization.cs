
using Team21.Acc.Core.Courses;

namespace Team21.Acc.Infrastructure.Courses.Authorization;

/// <summary>
/// Development-only authorization for course operations.
/// </summary>
public sealed class DevelopmentCourseAuthorization : ICourseAuthorization
{
    private static readonly Guid TestManagerId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Task<bool> CanCreateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(userId == TestManagerId);
    }

    public Task<bool> CanArchiveAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(userId == TestManagerId);
    }

    public Task<bool> CanViewAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(userId != Guid.Empty);
    }
}
