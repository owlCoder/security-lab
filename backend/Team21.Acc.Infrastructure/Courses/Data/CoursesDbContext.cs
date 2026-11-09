using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Courses.Data;

/// <summary>
/// ACC-01 tables, schema "courses".
/// </summary>
public class CoursesDbContext : TeamDbContextBase, ICoursesDbContext
{
    public const string SchemaName = "courses";

    public CoursesDbContext(DbContextOptions<CoursesDbContext> options)
        : base(options)
    {
    }

    protected override string Schema => SchemaName;

    public DbSet<Course> Courses => Set<Course>();
}
