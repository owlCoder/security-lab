using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Groups;
using Team21.Acc.Infrastructure.Groups.Data;

namespace Team21.Acc.Infrastructure.Groups.Services;

/// <summary>
/// EF Core implementacija <see cref="IMaterialAccessRuleReader"/> nad tabelama MaterialAudiences,
/// GroupMaterialAccess i Groups. Soft-delete filter sam izbacuje obrisane zapise i opozvane dozvole;
/// ovde se dodatno izbacuju dozvole za arhivirane grupe i za grupe drugog kursa.
/// </summary>
public class MaterialAccessRuleReader : IMaterialAccessRuleReader
{
    private readonly IGroupsDbContext _context;

    public MaterialAccessRuleReader(IGroupsDbContext context)
    {
        _context = context;
    }

    public async Task<MaterialAccessRule?> FindAsync(MaterialRef material, CancellationToken ct = default)
    {
        // Neispravna oznaka nikada ne daje pravilo (podrazumevano odbijanje, ACC-05).
        if (material is null || material.MaterialId == Guid.Empty || !Enum.IsDefined(material.Type))
            return null;

        var row = await Project(_context.MaterialAudiences
                .Where(a => a.MaterialType == material.Type && a.MaterialId == material.MaterialId))
            .SingleOrDefaultAsync(ct);

        return row is null ? null : ToRule(row);
    }

    public async Task<IReadOnlyList<MaterialAccessRule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default)
    {
        if (courseId == Guid.Empty)
            return [];

        var rows = await Project(_context.MaterialAudiences
                .Where(a => a.CourseId == courseId)
                .OrderBy(a => a.MaterialType)
                .ThenBy(a => a.MaterialId))
            .ToListAsync(ct);

        return rows.Select(ToRule).ToList();
    }

    // Jedan upit po pravilu zajedno sa dozvoljenim grupama; mapiranje u record radi se posle, u memoriji.
    private static IQueryable<RuleRow> Project(IQueryable<MaterialAudience> audiences) =>
        audiences
            .AsNoTracking()
            .Select(a => new RuleRow
            {
                MaterialType = a.MaterialType,
                MaterialId = a.MaterialId,
                CourseId = a.CourseId,
                Visibility = a.Visibility,
                AllowedGroupIds = a.GroupAccess
                    .Where(g => g.Group.Status == GroupStatus.Active && g.Group.CourseId == a.CourseId)
                    .Select(g => g.GroupId)
                    .ToList()
            });

    private static MaterialAccessRule ToRule(RuleRow row) =>
        new(new MaterialRef(row.MaterialType, row.MaterialId), row.CourseId, row.Visibility, row.AllowedGroupIds);

    private sealed class RuleRow
    {
        public MaterialType MaterialType { get; init; }
        public Guid MaterialId { get; init; }
        public Guid CourseId { get; init; }
        public MaterialVisibility Visibility { get; init; }
        public List<Guid> AllowedGroupIds { get; init; } = [];
    }
}
