namespace Team21.Acc.Core.Interfaces;

/// <summary>Result of a privileged action.</summary>
public enum AuditOutcome
{
    Success = 1,
    Denied = 2
}

/// <summary>
/// Records privileged actions (spec 2.1, AUD-01). The actor, time and request id are filled in by the
/// trail itself. Never pass content or secrets. Stands in for the AUD module until it is connected.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default);
}
