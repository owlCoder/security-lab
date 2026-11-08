namespace Platform.Api.Contracts.Audit;

/// <summary>One audit event as a teacher sees it. Details were validated against the catalog
/// allowlist when written: no secrets, no student answers, no free text.</summary>
public sealed record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorResponse Actor,
    string EventType,
    string Outcome,
    AuditResourceResponse? Resource,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Details);

public sealed record AuditActorResponse(string Kind, Guid? Id);

public sealed record AuditResourceResponse(string Type, string Id);

public sealed record AuditEventsPageResponse(
    IReadOnlyList<AuditEventResponse> Items,
    string? NextCursor);
