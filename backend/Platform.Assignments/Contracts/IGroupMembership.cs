namespace Platform.Assignments.Contracts;

public interface IGroupMembership
{
    ValueTask<bool> IsGroupMemberAsync(
        string studentId,
        string courseId,
        string groupId,
        CancellationToken cancellationToken = default);
}