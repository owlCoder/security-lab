using Microsoft.EntityFrameworkCore;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Infrastructure.Persistence;

/// <summary>
/// EF Core reader for the course audit view. <c>course_id = @course</c> and the time range are
/// always in the SQL, before sorting and paging, so another course's rows are never read, counted
/// or paged (T07). The order <c>occurred_at DESC, id DESC</c> follows the index
/// <c>ix_audit_events_course_time_id</c>. Only the DTO columns are selected; <c>details</c> stays
/// in the database.
/// </summary>
internal sealed class PostgresCourseAuditEventReader(AuditDbContext context) : ICourseAuditEventReader
{
    public async Task<IReadOnlyList<CourseAuditEventDto>> ReadPageAsync(
        NormalizedCourseAuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CourseId == Guid.Empty)
            throw new ArgumentException("A course audit read needs a course.", nameof(query));

        return await BuildQuery(context, query).ToListAsync(cancellationToken);
    }

    internal static IQueryable<CourseAuditEventDto> BuildQuery(AuditDbContext context, NormalizedCourseAuditQuery query)
    {
        var courseId = query.CourseId;
        var from = query.From;
        var to = query.To;

        var rows = context.Events.AsNoTracking()
            .Where(row => row.CourseId == courseId && row.OccurredAt >= from && row.OccurredAt < to);

        if (query.EventType is { } eventType)
            rows = rows.Where(row => row.EventType == eventType);

        if (query.Outcome is { } outcome)
            rows = rows.Where(row => row.Outcome == outcome);

        if (query.ActorId is { } actorId)
            rows = rows.Where(row => row.ActorId == actorId);

        if (query.CorrelationId is { } correlationId)
            rows = rows.Where(row => row.CorrelationId == correlationId);

        return rows
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id)
            .Skip(query.Offset)
            .Take(query.PageSize + 1)
            .Select(row => new CourseAuditEventDto(
                row.Id, row.OccurredAt, row.EventType, row.Outcome, row.ActorKind, row.ActorId,
                row.ResourceType, row.ResourceId, row.CorrelationId));
    }
}
