using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Infrastructure.Persistence;

/// <summary>
/// Read-only keyset query over audit.audit_events. The runtime role needs only SELECT
/// (already granted by scripts/audit/bootstrap-runtime.sql). Every value is a parameter; only
/// constant SQL fragments are concatenated. The course condition is always present.
/// </summary>
internal sealed class PostgresAuditEventQueryStore(AuditDbContext context) : IAuditEventQueryStore
{
    private const string BaseSql =
        "SELECT id, occurred_at, actor_kind, actor_id, event_type, outcome, course_id, " +
        "resource_type, resource_id, correlation_id, details, schema_version " +
        "FROM audit.audit_events WHERE course_id = @courseId";

    public async Task<IReadOnlyList<AuditEvent>> QueryCourseAsync(
        Guid courseId,
        AuditEventFilter filter,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (courseId == Guid.Empty)
            throw new ArgumentException("An empty GUID is not a valid course.", nameof(courseId));
        if (take < 1 || take > AuditReadLimits.MaxLimit + 1)
            throw new ArgumentOutOfRangeException(nameof(take));

        var sql = new StringBuilder(BaseSql);
        var parameters = new List<NpgsqlParameter>
        {
            new("courseId", NpgsqlDbType.Uuid) { Value = courseId }
        };

        if (filter.EventType is not null)
        {
            sql.Append(" AND event_type = @eventType");
            parameters.Add(new NpgsqlParameter("eventType", NpgsqlDbType.Text) { Value = filter.EventType });
        }

        if (filter.Outcome is { } outcome)
        {
            sql.Append(" AND outcome = @outcome");
            parameters.Add(new NpgsqlParameter("outcome", NpgsqlDbType.Text) { Value = outcome.ToString() });
        }

        if (filter.From is { } fromValue)
        {
            sql.Append(" AND occurred_at >= @from");
            parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = fromValue.ToUniversalTime() });
        }

        if (filter.To is { } toValue)
        {
            sql.Append(" AND occurred_at <= @to");
            parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = toValue.ToUniversalTime() });
        }

        if (filter.After is { } after)
        {
            // Row comparison matches the (occurred_at DESC, id DESC) order below.
            sql.Append(" AND (occurred_at, id) < (@afterAt, @afterId)");
            parameters.Add(new NpgsqlParameter("afterAt", NpgsqlDbType.TimestampTz) { Value = after.OccurredAt.ToUniversalTime() });
            parameters.Add(new NpgsqlParameter("afterId", NpgsqlDbType.Uuid) { Value = after.Id });
        }

        var rows = await context.Events
            .FromSqlRaw(sql.ToString(), parameters.Cast<object>().ToArray())
            .AsNoTracking()
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }
}
