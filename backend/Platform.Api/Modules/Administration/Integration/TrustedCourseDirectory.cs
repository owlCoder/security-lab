using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Api.Modules.Access;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Platform.Api.Modules.Administration.Integration;

public sealed record TrustedCourse(Guid Id, string Name, bool Active);
public interface ITrustedCourseDirectory
{
    ValueTask<PortResult<TrustedCourse>> FindAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Read-only adapter over ACC-01's published context; never writes catalog tables.</summary>
public sealed class CatalogCourseDirectory(IServiceScopeFactory scopes) : ITrustedCourseDirectory
{
    public async ValueTask<PortResult<TrustedCourse>> FindAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return PortResult<TrustedCourse>.Missing();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ICoursesDbContext>();
            var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
            return course is null ? PortResult<TrustedCourse>.Missing()
                : PortResult<TrustedCourse>.Found(new(course.Id, course.Name, course.Status == CourseStatus.Active));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or IOException or TimeoutException or InvalidOperationException)
        { return PortResult<TrustedCourse>.Offline(); }
    }
}
