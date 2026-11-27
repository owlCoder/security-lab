using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Infrastructure.Courses.Services;

/// <summary>
/// Reads the persisted course on every call, so an archive is seen by the very next enrollment.
/// Soft deleted courses drop out through the query filter and answer NotFound.
/// </summary>
public sealed class CourseEnrollmentPolicy : ICourseEnrollmentPolicy
{
    private readonly ICoursesDbContext _context;

    public CourseEnrollmentPolicy(ICoursesDbContext context)
    {
        _context = context;
    }

    public async Task<CourseEnrollmentDecision> CheckAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        if (courseId == Guid.Empty)
            return CourseEnrollmentDecision.NotFound;

        var status = await _context.Courses
            .AsNoTracking()
            .Where(course => course.Id == courseId)
            .Select(course => (CourseStatus?)course.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return status switch
        {
            null => CourseEnrollmentDecision.NotFound,
            CourseStatus.Active => CourseEnrollmentDecision.Open,
            _ => CourseEnrollmentDecision.Archived
        };
    }
}
