namespace Team21.Acc.Core.Groups;

/// <summary>
/// Aktivno članstvo korisnika u jednoj aktivnoj grupi kursa, kako ga vraća <see cref="IGroupMembershipReader"/>.
/// Ovo je samo podatak za čitanje (DTO), a ne entitet; tabela je <see cref="GroupMember"/>.
/// </summary>
/// <param name="JoinedAt">Kada je korisnik dodat u grupu (CreatedAt reda u GroupMembers), u UTC-u.</param>
public sealed record GroupMembership(
    Guid GroupId,
    string GroupName,
    Guid CourseId,
    DateTime JoinedAt);
