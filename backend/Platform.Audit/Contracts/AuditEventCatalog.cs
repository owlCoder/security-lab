using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Platform.Audit.Contracts;

/// <summary>
/// Catalog schema version 1: every event type the Audit module accepts and what each one
/// allows. Anything not listed here is rejected. See docs/components/audit/CONTRACTS.md.
/// </summary>
public static class AuditEventCatalog
{
    public const int SchemaVersion = 1;
    public const int MaxDetailKeys = 20;
    public const int MaxDetailsBytes = 2048;
    public const int MaxDetailValueLength = 64;

    private static readonly IReadOnlySet<AuditActorKind> User = Kinds(AuditActorKind.User);
    private static readonly IReadOnlySet<AuditActorKind> Service = Kinds(AuditActorKind.Service);
    private static readonly IReadOnlySet<AuditActorKind> Anonymous = Kinds(AuditActorKind.Anonymous);
    private static readonly IReadOnlySet<AuditActorKind> UserOrAnonymous =
        Kinds(AuditActorKind.User, AuditActorKind.Anonymous);

    private static readonly AuditDetailField ReasonCode = new(
        AuditDetailFormat.Code,
        new[] { AuditOutcome.Denied, AuditOutcome.Failure }.ToFrozenSet());

    // Privileged changes: denied for an anonymous caller too, failed only for an authenticated one.
    private static readonly IReadOnlyDictionary<AuditOutcome, IReadOnlySet<AuditActorKind>> PrivilegedChange =
        Outcomes(
            (AuditOutcome.Success, User),
            (AuditOutcome.Denied, UserOrAnonymous),
            (AuditOutcome.Failure, User));

    public static IReadOnlyDictionary<string, AuditEventSchema> Schemas { get; } = new[]
    {
        new AuditEventSchema(
            AuditEventTypes.AuthLogin,
            Outcomes((AuditOutcome.Success, User), (AuditOutcome.Failure, UserOrAnonymous)),
            AuditPresence.Forbidden,
            AuditPresence.Optional,
            AuditResourceTypes.Account,
            Fields(("reasonCode", ReasonCode))),

        new AuditEventSchema(
            AuditEventTypes.AuthLogout,
            Outcomes((AuditOutcome.Success, User)),
            AuditPresence.Forbidden,
            AuditPresence.Forbidden,
            null,
            Fields()),

        new AuditEventSchema(
            AuditEventTypes.AuthLoginThrottled,
            Outcomes((AuditOutcome.Denied, Anonymous)),
            AuditPresence.Forbidden,
            AuditPresence.Forbidden,
            null,
            Fields()),

        new AuditEventSchema(
            AuditEventTypes.AccountStatusChanged,
            PrivilegedChange,
            AuditPresence.Forbidden,
            AuditPresence.Required,
            AuditResourceTypes.Account,
            Fields(
                ("oldStatus", new AuditDetailField(AuditDetailFormat.AccountStatus)),
                ("newStatus", new AuditDetailField(AuditDetailFormat.AccountStatus)),
                ("reasonCode", ReasonCode))),

        new AuditEventSchema(
            AuditEventTypes.CourseMemberRoleChanged,
            PrivilegedChange,
            AuditPresence.Required,
            AuditPresence.Required,
            AuditResourceTypes.Account,
            Fields(
                ("oldRole", new AuditDetailField(AuditDetailFormat.CourseRole)),
                ("newRole", new AuditDetailField(AuditDetailFormat.CourseRole)),
                ("reasonCode", ReasonCode))),

        Delegation(AuditEventTypes.CourseDelegationGranted),
        Delegation(AuditEventTypes.CourseDelegationRevoked),

        new AuditEventSchema(
            AuditEventTypes.CourseDelegationExpired,
            Outcomes((AuditOutcome.Success, Service)),
            AuditPresence.Required,
            AuditPresence.Required,
            AuditResourceTypes.Account,
            Fields(
                ("action", new AuditDetailField(AuditDetailFormat.ActionId)),
                ("delegationId", new AuditDetailField(AuditDetailFormat.Uuid)))),

        new AuditEventSchema(
            AuditEventTypes.AccessDenied,
            Outcomes((AuditOutcome.Denied, UserOrAnonymous)),
            AuditPresence.Optional,
            AuditPresence.Optional,
            null,
            Fields(
                ("action", new AuditDetailField(AuditDetailFormat.ActionId)),
                ("denialReason", new AuditDetailField(AuditDetailFormat.AccessDenialReason)),
                ("policyId", new AuditDetailField(AuditDetailFormat.PolicyId)),
                ("policyVersion", new AuditDetailField(AuditDetailFormat.PolicyVersion)))),

        new AuditEventSchema(
            AuditEventTypes.AuditEventsViewed,
            Outcomes((AuditOutcome.Success, User)),
            AuditPresence.Required,
            AuditPresence.Required,
            AuditResourceTypes.Course,
            Fields(
                ("filterType", new AuditDetailField(AuditDetailFormat.ActionId)),
                ("filterOutcome", new AuditDetailField(AuditDetailFormat.OutcomeName)),
                ("filterFrom", new AuditDetailField(AuditDetailFormat.UtcTimestamp)),
                ("filterTo", new AuditDetailField(AuditDetailFormat.UtcTimestamp)),
                ("resultCount", new AuditDetailField(AuditDetailFormat.Count)))),

        Revision(AuditEventTypes.RevisionSubmitted),
        Revision(AuditEventTypes.RevisionApproved),
        Revision(AuditEventTypes.RevisionReturned),
    }.ToFrozenDictionary(schema => schema.EventType, StringComparer.Ordinal);

    public static bool TryGet(string? eventType, [NotNullWhen(true)] out AuditEventSchema? schema)
    {
        schema = null;
        return eventType is not null && Schemas.TryGetValue(eventType, out schema);
    }

    private static AuditEventSchema Delegation(string eventType) => new(
        eventType,
        PrivilegedChange,
        AuditPresence.Required,
        AuditPresence.Required,
        AuditResourceTypes.Account,
        Fields(
            ("action", new AuditDetailField(AuditDetailFormat.ActionId)),
            ("delegationId", new AuditDetailField(AuditDetailFormat.Uuid)),
            ("reasonCode", ReasonCode)));

    private static AuditEventSchema Revision(string eventType) => new(eventType,
        Outcomes((AuditOutcome.Success, User)), AuditPresence.Required, AuditPresence.Required,
        AuditResourceTypes.Revision, Fields(
            ("authorId", new AuditDetailField(AuditDetailFormat.Uuid)),
            ("operationId", new AuditDetailField(AuditDetailFormat.Uuid)),
            ("contentHash", new AuditDetailField(AuditDetailFormat.Sha256)),
            ("laboratoryId", new AuditDetailField(AuditDetailFormat.Uuid)),
            ("revisionNumber", new AuditDetailField(AuditDetailFormat.RevisionNumber))),
        (eventType == AuditEventTypes.RevisionApproved
            ? new[] { "authorId", "operationId", "contentHash", "laboratoryId", "revisionNumber" }
            : new[] { "authorId", "operationId", "contentHash" }).ToFrozenSet(StringComparer.Ordinal));

    private static IReadOnlySet<AuditActorKind> Kinds(params AuditActorKind[] kinds) => kinds.ToFrozenSet();

    private static IReadOnlyDictionary<AuditOutcome, IReadOnlySet<AuditActorKind>> Outcomes(
        params (AuditOutcome Outcome, IReadOnlySet<AuditActorKind> Actors)[] entries) =>
        entries.ToFrozenDictionary(entry => entry.Outcome, entry => entry.Actors);

    private static IReadOnlyDictionary<string, AuditDetailField> Fields(
        params (string Key, AuditDetailField Field)[] entries) =>
        entries.ToFrozenDictionary(entry => entry.Key, entry => entry.Field, StringComparer.Ordinal);
}
