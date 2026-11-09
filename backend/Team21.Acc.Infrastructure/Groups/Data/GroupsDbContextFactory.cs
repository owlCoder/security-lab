using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Groups.Data;

/// <summary>Lets dotnet ef build a GroupsDbContext without starting the api.</summary>
public class GroupsDbContextFactory : IDesignTimeDbContextFactory<GroupsDbContext>
{
    public GroupsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<GroupsDbContext>();
        builder.UseTeamNpgsql(DesignTimeConfiguration.ConnectionString(), GroupsDbContext.SchemaName);

        return new GroupsDbContext(builder.Options);
    }
}
