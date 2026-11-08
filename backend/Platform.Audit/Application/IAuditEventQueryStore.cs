using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Application;

/// <summary>Read side of the append-only store. Always scoped to exactly one course.</summary>
internal interface IAuditEventQueryStore
{
    /// <summary>Newest first (occurred_at, then id), at most <paramref name="take"/> rows.</summary>
    Task<IReadOnlyList<AuditEvent>> QueryCourseAsync(
        Guid courseId,
        AuditEventFilter filter,
        int take,
        CancellationToken cancellationToken = default);
}

internal sealed record AuditEventFilter(
    string? EventType,
    AuditOutcome? Outcome,
    DateTimeOffset? From,
    DateTimeOffset? To,
    AuditCursor? After);
