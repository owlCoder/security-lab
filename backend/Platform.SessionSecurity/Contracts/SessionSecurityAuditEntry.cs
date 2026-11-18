using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// AUD-01: actor, event, time and outcome of a session security action.
/// AUD-03: has no field for a token, a token hash or a chain ID.
/// </summary>
/// <param name="EventType">One of <see cref="SessionSecurityAuditEvents"/>.</param>
/// <param name="SubjectId">Actor; null when the session belongs to no user.</param>
/// <param name="Trigger">Why the rotation was requested.</param>
/// <param name="Outcome">One of <see cref="SessionRotationCodes"/>.</param>
/// <param name="OccurredAt">Server time of the action.</param>
/// <param name="CorrelationId">Request correlation ID (AUD-04).</param>
public sealed record SessionSecurityAuditEntry(
    string EventType,
    string? SubjectId,
    RotationTrigger Trigger,
    string Outcome,
    DateTimeOffset OccurredAt,
    string CorrelationId);
