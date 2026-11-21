using Platform.Membership.Contracts;

namespace Platform.Membership.Infrastructure.Courses;

/// <summary>
/// PRIVREMENO (ACC-02): registruje se samo u Development okruženju.
/// Zameniti adapterom nad ugovorom Tima 21 (ACC-01) kada ga izlože.
/// </summary>
public sealed class DevelopmentCourseReader : ICourseReader
{
    private static readonly IReadOnlyDictionary<Guid, CourseInfo> Courses = new Dictionary<Guid, CourseInfo>
    {
        [DevelopmentCourses.SecurityLab] = new(DevelopmentCourses.SecurityLab, IsArchived: false),
        [DevelopmentCourses.ArchivedCourse] = new(DevelopmentCourses.ArchivedCourse, IsArchived: true),
        [DevelopmentCourses.OtherCourse] = new(DevelopmentCourses.OtherCourse, IsArchived: false)
    };

    public ValueTask<CourseInfo?> FindAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Courses.GetValueOrDefault(courseId));
    }
}