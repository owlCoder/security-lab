using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Courses.Data;

/// <summary>Lets dotnet ef build a CoursesDbContext without starting the api.</summary>
public class CoursesDbContextFactory : IDesignTimeDbContextFactory<CoursesDbContext>
{
    public CoursesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CoursesDbContext>();
        builder.UseTeamNpgsql(DesignTimeConfiguration.ConnectionString(), CoursesDbContext.SchemaName);

        return new CoursesDbContext(builder.Options);
    }
}
