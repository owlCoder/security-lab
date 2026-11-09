using Microsoft.EntityFrameworkCore;
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

    // public DbSet<Group> Groups { get; set; }
}
