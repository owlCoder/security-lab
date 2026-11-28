using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Infrastructure.Courses.Services;

/// <summary>
/// Gives ACC-06 the state of an ACC-01 course. A missing course or an unknown status is null,
/// and ACC-06 denies access for it.
/// </summary>
public sealed class CourseStateReader : ICourseStateReader
{
    private readonly ICoursesDbContext _context;

    public CourseStateReader(ICoursesDbContext context)
    {
        _context = context;
    }

    public async Task<CourseState?> GetStateAsync(Guid courseId, CancellationToken ct = default)
    {
        var status = await _context.Courses
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => (CourseStatus?)c.Status)
            .FirstOrDefaultAsync(ct);

        return status switch
        {
            CourseStatus.Active => CourseState.Active,
            CourseStatus.Archived => CourseState.Archived,
            _ => null
        };
    }
}
