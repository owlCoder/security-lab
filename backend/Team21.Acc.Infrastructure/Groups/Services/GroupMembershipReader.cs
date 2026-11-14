using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Groups;
using Team21.Acc.Infrastructure.Groups.Data;

namespace Team21.Acc.Infrastructure.Groups.Services;

/// <summary>
/// EF Core implementacija <see cref="IGroupMembershipReader"/> nad tabelama GroupMembers i Groups.
/// Soft-delete filter iz TeamDbContextBase sam izbacuje uklonjene članove i obrisane grupe.
/// </summary>
public class GroupMembershipReader : IGroupMembershipReader
{
    private readonly IGroupsDbContext _context;

    public GroupMembershipReader(IGroupsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<GroupMembership>> GroupsOfAsync(Guid accountId, Guid courseId, CancellationToken ct = default)
    {
        // Prazan ID nikada ne odgovara stvarnom nalogu ili kursu: odmah prazna lista, bez upita.
        if (accountId == Guid.Empty || courseId == Guid.Empty)
            return [];

        return await _context.GroupMembers
            .AsNoTracking()
            .Where(m => m.UserId == accountId
                     && m.Group.CourseId == courseId
                     && m.Group.Status == GroupStatus.Active)
            .OrderBy(m => m.Group.Name)
            .Select(m => new GroupMembership(m.GroupId, m.Group.Name, m.Group.CourseId, m.CreatedAt))
            .ToListAsync(ct);
    }
}
