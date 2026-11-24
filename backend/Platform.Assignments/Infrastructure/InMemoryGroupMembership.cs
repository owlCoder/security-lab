using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.ValueObjects;

namespace Platform.Assignments.Infrastructure;

public sealed class InMemoryGroupMembership : IGroupMembership
{
    private readonly object _sync = new();
    private readonly HashSet<(string CourseId, string GroupId, string StudentId)> _members = new();

    public void Add(string courseId, string groupId, string studentId)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            throw new ArgumentException("Course ID is required.", nameof(courseId));
        }

        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException("Group ID is required.", nameof(groupId));
        }

        if (!PrincipalId.IsValid(studentId))
        {
            throw new ArgumentException("Student ID is not valid.", nameof(studentId));
        }

        lock (_sync)
        {
            _members.Add((courseId, groupId, studentId));
        }
    }

    public ValueTask<bool> IsGroupMemberAsync(
        string studentId,
        string courseId,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(studentId) ||
            string.IsNullOrWhiteSpace(courseId) ||
            string.IsNullOrWhiteSpace(groupId))
        {
            return ValueTask.FromResult(false);
        }

        lock (_sync)
        {
            return ValueTask.FromResult(_members.Contains((courseId, groupId, studentId)));
        }
    }
}