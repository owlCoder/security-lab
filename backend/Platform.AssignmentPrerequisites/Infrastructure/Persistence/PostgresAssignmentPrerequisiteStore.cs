using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public sealed class PostgresAssignmentPrerequisiteStore : IAssignmentPrerequisiteStore
{
    private readonly AssignmentPrerequisitesDbContext _db;

    public PostgresAssignmentPrerequisiteStore(AssignmentPrerequisitesDbContext db)
    {
        _db = db;
    }

    public async ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForCourseAsync(
        string courseId,
        CancellationToken cancellationToken = default)
    {
        List<AssignmentPrerequisiteRow> rows = await _db.Prerequisites
            .AsNoTracking()
            .Where(p => p.CourseId == courseId)
            .OrderBy(p => p.AssignmentId)
            .ThenBy(p => p.RequiredAssignmentId)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<IReadOnlyList<AssignmentPrerequisite>> GetForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        List<AssignmentPrerequisiteRow> rows = await _db.Prerequisites
            .AsNoTracking()
            .Where(p => p.AssignmentId == assignmentId)
            .OrderBy(p => p.RequiredAssignmentId)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<bool> AddAsync(
        AssignmentPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);

                var entry = _db.Prerequisites.Add(ToRow(prerequisite));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async ValueTask<bool> RemoveAsync(
        Guid assignmentId,
        Guid requiredAssignmentId,
        CancellationToken cancellationToken = default)
    {
        int deleted = await _db.Prerequisites
            .Where(p => p.AssignmentId == assignmentId && p.RequiredAssignmentId == requiredAssignmentId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    private static AssignmentPrerequisiteRow ToRow(AssignmentPrerequisite prerequisite) => new()
    {
        CourseId = prerequisite.CourseId,
        AssignmentId = prerequisite.AssignmentId,
        RequiredAssignmentId = prerequisite.RequiredAssignmentId,
        CompletionRequirement = (short)prerequisite.CompletionRequirement,
        CreatedById = prerequisite.CreatedById,
        CreatedAtUtc = prerequisite.CreatedAtUtc
    };

    private static AssignmentPrerequisite ToDomain(AssignmentPrerequisiteRow row) => new(
        row.CourseId,
        row.AssignmentId,
        row.RequiredAssignmentId,
        (CompletionRequirement)row.CompletionRequirement,
        row.CreatedById,
        row.CreatedAtUtc);
}
