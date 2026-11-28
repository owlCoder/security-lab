using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Fakes;

/// <summary>Development only. Stands in for ACC-02/03 enrollment and teacher assignment.</summary>
public sealed class FakeCourseMembershipReader : ICourseMembershipReader
{
    private readonly FakeDirectory _directory;

    public FakeCourseMembershipReader(FakeDirectory directory)
    {
        _directory = directory;
    }

    public Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default) =>
        Task.FromResult(_directory.MembershipOf(accountId, courseId));
}
