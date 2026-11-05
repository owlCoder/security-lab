using Microsoft.EntityFrameworkCore;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Infrastructure.Persistence;

internal sealed class PostgresAuditEventStore(AuditDbContext context) : IAuditEventStore
{
    public async Task<AuditAppendStatus> AppendAsync(
        AuditEvent record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var draft = new AuditEventDraft(record.EventType, record.Outcome, record.CourseId,
            record.ResourceType is null ? null : new AuditResourceRef(record.ResourceType, record.ResourceId!),
            record.Details, record.Id);
        if (record.SchemaVersion != AuditEventCatalog.SchemaVersion
            || new AuditDraftValidator().Validate(draft,
                new AuditContext(new AuditActor(record.ActorKind, record.ActorId), record.CorrelationId))
                != AuditRejectionCode.None)
            throw new ArgumentException("The audit record does not match the supported schema.", nameof(record));

        var row = AuditEventRow.FromDomain(record);
        // ON CONFLICT keeps a retry from aborting the caller's PostgreSQL transaction.
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit.audit_events
                (id, occurred_at, actor_kind, actor_id, event_type, outcome, course_id,
                 resource_type, resource_id, correlation_id, details, schema_version)
            VALUES
                ({row.Id}, {row.OccurredAt}, {row.ActorKind.ToString()}, {row.ActorId},
                 {row.EventType}, {row.Outcome.ToString()}, {row.CourseId}, {row.ResourceType},
                 {row.ResourceId}, {row.CorrelationId}, CAST({row.DetailsJson} AS jsonb), {row.SchemaVersion})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);

        if (inserted == 1)
            return AuditAppendStatus.Appended;

        var existing = await FindAsync(record.Id, cancellationToken);
        return existing is not null && existing.HasSameContentAs(record)
            ? AuditAppendStatus.AlreadyPresent
            : AuditAppendStatus.Conflict;
    }

    public async Task<AuditEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Events.AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == id, cancellationToken);
        return row?.ToDomain();
    }
}
