namespace Platform.Access.Infrastructure.Auditing;

internal sealed record AccessAuditRecord(
    DateTimeOffset Time,
    string EventType,
    string Actor,
    string Action,
    string Target,
    string Outcome,
    string CorrelationId,
    string? DenialReason,
    string? PolicyId,
    string? PolicyVersion,
    int Count = 1)
{
    public const string DeniedEventType = "access.denied";
    public const string PrivilegedAllowedEventType = "access.allowed.privileged";
    public const string Anonymous = "anonymous";
    public const string Invalid = "invalid";
    public const string NoValue = "-";
}
