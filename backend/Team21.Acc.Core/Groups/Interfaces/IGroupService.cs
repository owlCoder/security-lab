using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups.Dtos;

namespace Team21.Acc.Core.Groups.Interfaces;

/// <summary>Group management for the teacher of a course (ACC-06).</summary>
public interface IGroupService
{
    Task<Result<GroupDto>> CreateGroupAsync(Guid courseId, string name, CancellationToken ct = default);

    Task<Result<IReadOnlyList<GroupDto>>> GetGroupsAsync(Guid courseId, CancellationToken ct = default);

    Task<Result<GroupDto>> GetGroupAsync(Guid courseId, Guid groupId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<GroupMemberDto>>> GetMembersAsync(Guid courseId, Guid groupId, CancellationToken ct = default);

    Task<Result<GroupMemberDto>> AddMemberAsync(Guid courseId, Guid groupId, Guid userId, CancellationToken ct = default);

    Task<Result> RemoveMemberAsync(Guid courseId, Guid groupId, Guid userId, CancellationToken ct = default);
}
