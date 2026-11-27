using Team21.Acc.Core.Courses;

namespace Team21.Acc.Infrastructure.Courses.Authorization;

/// <summary>
/// Development only. The demo teacher may create and archive any course,
/// any signed-in user may read courses.
/// </summary>
public sealed class DevelopmentCourseAuthorization : ICourseAuthorization
{
    // Same id as the ACC-06 demo teacher
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
