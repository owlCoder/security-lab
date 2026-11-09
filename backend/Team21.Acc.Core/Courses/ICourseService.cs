
using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Courses;

/// <summary>Creates, archives, and reads courses for the current caller.</summary>
public interface ICourseService
{
    Task<Result<CourseDto>> CreateAsync(
        CreateCourseRequest request,
        CancellationToken cancellationToken = default
    );

    Task<Result> ArchiveAsync(
        Guid courseId,
        CancellationToken cancellationToken = default
    );

    Task<Result<IReadOnlyList<CourseDto>>> GetCoursesAsync(
        CancellationToken cancellationToken = default
    );

    Task<Result<CourseDto>> GetCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default
    );
}
