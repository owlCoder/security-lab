namespace Platform.Api.Models.Identity;

public sealed record AuditEvent(
    DateTimeOffset Time,
    Guid? ActorId,
    string Action,
    string Target,
    string Outcome,
    string CorrelationId,
    string? Detail);
