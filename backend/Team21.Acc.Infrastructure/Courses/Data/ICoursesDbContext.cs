using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Courses.Data;

/// <summary>What ACC-01 services get injected instead of CoursesDbContext.</summary>
public interface ICoursesDbContext : ITeamDbContext
{
    DbSet<Course> Courses { get; }
}
