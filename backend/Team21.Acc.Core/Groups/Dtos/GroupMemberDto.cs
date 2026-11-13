namespace Team21.Acc.Core.Groups.Dtos;

/// <summary>A current member of a group. JoinedAt is when they were added, in UTC.</summary>
public sealed record GroupMemberDto(
    Guid Id,
    Guid GroupId,
    Guid UserId,
    DateTime JoinedAt);
