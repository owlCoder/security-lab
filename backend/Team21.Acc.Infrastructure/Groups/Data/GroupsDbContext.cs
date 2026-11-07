using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Groups;
using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Groups.Data;

/// <summary>
/// ACC-06 tables, schema "groups". No foreign keys into the courses schema.
/// </summary>
public class GroupsDbContext : TeamDbContextBase, IGroupsDbContext
{
    public const string SchemaName = "groups";

    public GroupsDbContext(DbContextOptions<GroupsDbContext> options)
        : base(options)
    {
    }

    protected override string Schema => SchemaName;

    // Konfiguracije su u Groups/Configurations i učitavaju se same (TeamDbContextBase.OnModelCreating).
    public DbSet<Group> Groups { get; set; } = null!;

    public DbSet<GroupMember> GroupMembers { get; set; } = null!;

    public DbSet<MaterialAudience> MaterialAudiences { get; set; } = null!;

    public DbSet<GroupMaterialAccess> GroupMaterialAccesses { get; set; } = null!;
}
