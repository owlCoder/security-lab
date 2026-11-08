namespace Platform.Audit.Contracts;

/// <summary>
/// Read model of one stored audit event (AUD-06). Details were validated against the catalog
/// allowlist when the event was written, so they contain no secrets or free text.
/// </summary>
public sealed record AuditEventView(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorKind ActorKind,
    Guid? ActorId,
    string EventType,
    AuditOutcome Outcome,
    Guid? CourseId,
    string? ResourceType,
    string? ResourceId,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Details);
