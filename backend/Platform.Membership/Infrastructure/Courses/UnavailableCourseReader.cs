using Platform.Membership.Contracts;

namespace Platform.Membership.Infrastructure.Courses;

/// <summary>
/// Van Development okruženja, dok ne postoji pravi izvor kurseva: nijedan kurs "ne postoji",
/// pa nijedan upis ne prolazi (fail-closed).
/// </summary>
public sealed class UnavailableCourseReader : ICourseReader
{
    public ValueTask<CourseInfo?> FindAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<CourseInfo?>(null);
    }
}