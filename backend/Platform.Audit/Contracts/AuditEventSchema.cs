namespace Platform.Audit.Contracts;

/// <summary>
/// What one event type allows. The allowed outcomes are the keys of
/// <see cref="ActorsByOutcome"/>; each outcome lists the actor kinds allowed with it.
/// </summary>
/// <param name="ResourceType">Required resource type, or null for any of <see cref="AuditResourceTypes"/>.</param>
public sealed record AuditEventSchema(
    string EventType,
    IReadOnlyDictionary<AuditOutcome, IReadOnlySet<AuditActorKind>> ActorsByOutcome,
    AuditPresence Course,
    AuditPresence Resource,
    string? ResourceType,
    IReadOnlyDictionary<string, AuditDetailField> Details);

public enum AuditPresence
{
    Required,
    Optional,
    Forbidden
}

/// <param name="OnlyForOutcomes">Outcomes with which the field may appear, or null for all.</param>
public sealed record AuditDetailField(
    AuditDetailFormat Format,
    IReadOnlySet<AuditOutcome>? OnlyForOutcomes = null);

public enum AuditDetailFormat
{
    /// <summary><c>^[a-z][a-z0-9_]{0,31}$</c></summary>
    Code,

    /// <summary>Action or event type, same rule as <c>ActionId</c> in Platform.Access.</summary>
    ActionId,

    /// <summary><c>active</c>, <c>blocked</c> or <c>archived</c>.</summary>
    AccountStatus,

    /// <summary><c>none</c>, <c>student</c>, <c>teacher</c> or <c>assistant</c>.</summary>
    CourseRole,

    /// <summary>A denial reason of Platform.Access in snake_case.</summary>
    AccessDenialReason,

    /// <summary><c>^[a-z][a-z0-9_.-]{0,63}$</c></summary>
    PolicyId,

    /// <summary><c>^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}$</c></summary>
    PolicyVersion,

    /// <summary>A non-empty UUID.</summary>
    Uuid,

    /// <summary><c>success</c>, <c>denied</c> or <c>failure</c>.</summary>
    OutcomeName,

    /// <summary>ISO 8601 UTC timestamp ending with <c>Z</c>.</summary>
    UtcTimestamp,

    /// <summary>Integer from 0 to 100000.</summary>
    Count
}
