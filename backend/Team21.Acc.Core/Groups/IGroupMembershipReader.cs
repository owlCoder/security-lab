namespace Team21.Acc.Core.Groups;

/// <summary>
/// Čitanje članstava u grupama (ACC-06). Samo čitanje, bez provere prava;
/// koristi ga serverska provera vidljivosti i servis grupa. Ne izlagati direktno kroz endpoint.
/// </summary>
public interface IGroupMembershipReader
{
    /// <summary>
    /// Aktivna članstva naloga <paramref name="accountId"/> u aktivnim, neobrisanim grupama kursa
    /// <paramref name="courseId"/>, poređana po nazivu grupe. Uklonjeni članovi i arhivirane grupe
    /// se ne vraćaju. Prazna lista ako ih nema.
    /// </summary>
    Task<IReadOnlyList<GroupMembership>> GroupsOfAsync(Guid accountId, Guid courseId, CancellationToken ct = default);
}
