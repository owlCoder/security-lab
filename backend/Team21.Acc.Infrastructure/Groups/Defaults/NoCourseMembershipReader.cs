using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Defaults;

/// <summary>
/// Used outside Development until the ACC-02/03 modules are connected. Nobody is a course member.
/// </summary>
public sealed class NoCourseMembershipReader : ICourseMembershipReader
{
    public Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default) =>
        Task.FromResult<CourseMembership?>(null);
}
