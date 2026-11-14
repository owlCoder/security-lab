namespace Team21.Acc.Core.Groups.Visibility.Interfaces;

/// <summary>Reads an account's role in a course, null when not a member. Owned by ACC-02/03.</summary>
public interface ICourseMembershipReader
{
    Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default);
}
