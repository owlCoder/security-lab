using Platform.Audit.Contracts;

namespace Platform.Audit.Tests;

// One valid draft per catalog event type, with a matching trusted context.
// Synthetic identifiers only; no real accounts or courses.
internal static class AuditDraftExamples
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TargetAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ServiceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid CourseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid DelegationId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public const string CorrelationId = "corr-0001";

    public static AuditContext UserContext { get; } = new(AuditActor.ForUser(UserId), CorrelationId);
    public static AuditContext ServiceContext { get; } = new(AuditActor.ForService(ServiceId), CorrelationId);
    public static AuditContext AnonymousContext { get; } = new(AuditActor.Anonymous, CorrelationId);

    public static AuditResourceRef TargetAccount { get; } =
        new(AuditResourceTypes.Account, TargetAccountId.ToString());

    public static AuditResourceRef Course { get; } =
        new(AuditResourceTypes.Course, CourseId.ToString());

    public static IEnumerable<(AuditEventDraft Draft, AuditContext Context)> ValidPerEventType()
    {
        yield return (
            new AuditEventDraft(AuditEventTypes.AuthLogin, AuditOutcome.Success, Resource: TargetAccount),
            UserContext);

        yield return (
            new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success),
            UserContext);

        yield return (
            new AuditEventDraft(AuditEventTypes.AuthLoginThrottled, AuditOutcome.Denied),
            AnonymousContext);

        yield return (StatusChange(), UserContext);

        yield return (
            new AuditEventDraft(
                AuditEventTypes.CourseMemberRoleChanged,
                AuditOutcome.Success,
                CourseId,
                TargetAccount,
                Details(("oldRole", "student"), ("newRole", "assistant"))),
            UserContext);

        yield return (Delegation(AuditEventTypes.CourseDelegationGranted), UserContext);
        yield return (Delegation(AuditEventTypes.CourseDelegationRevoked), UserContext);
        yield return (Delegation(AuditEventTypes.CourseDelegationExpired), ServiceContext);

        yield return (
            new AuditEventDraft(
                AuditEventTypes.AccessDenied,
                AuditOutcome.Denied,
                Details: Details(
                    ("action", "audit.events.read"),
                    ("denialReason", "no_policy"),
                    ("policyVersion", "v0"))),
            UserContext);

        yield return (
            new AuditEventDraft(
                AuditEventTypes.AuditEventsViewed,
                AuditOutcome.Success,
                CourseId,
                Course,
                Details(
                    ("filterType", "auth.login"),
                    ("filterOutcome", "denied"),
                    ("filterFrom", "2026-11-01T00:00:00Z"),
                    ("filterTo", "2026-11-08T00:00:00Z"),
                    ("resultCount", "3"))),
            UserContext);
    }

    public static AuditEventDraft StatusChange(
        AuditOutcome outcome = AuditOutcome.Success,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(
            AuditEventTypes.AccountStatusChanged,
            outcome,
            Resource: TargetAccount,
            Details: details ?? Details(("oldStatus", "active"), ("newStatus", "blocked")));

    public static IReadOnlyDictionary<string, string> Details(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static AuditEventDraft Delegation(string eventType) =>
        new(
            eventType,
            AuditOutcome.Success,
            CourseId,
            TargetAccount,
            Details(("action", "audit.events.read"), ("delegationId", DelegationId.ToString())));
}
