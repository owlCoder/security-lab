namespace Platform.Assignments.Contracts;

// ASN-06 TIM 15
// What happened. It deliberately has no actor and no time: the audit adapter takes both from the
// trusted server context (see docs/components/audit/CONTRACTS.md). It never holds free text.
public sealed record DeadlineExtensionAuditEntry(
    DeadlineExtensionAuditOutcome Outcome,
    Guid AssignmentId,
    string? CourseId,
    string? StudentId,
    DateTimeOffset? PreviousDueAtUtc,
    DateTimeOffset? NewDueAtUtc,
    string? ReasonCode);

public enum DeadlineExtensionAuditOutcome
{
    Success = 1,
    Denied = 2,
    Failure = 3
}

// Same format as reasonCode in the Audit catalog: ^[a-z][a-z0-9_]{0,31}$
public static class DeadlineExtensionReasonCodes
{
    public const string Forbidden = "forbidden";
    public const string InvalidStudent = "invalid_student";
    public const string Archived = "archived";
    public const string NotEnrolled = "not_enrolled";
    public const string InvalidDeadline = "invalid_deadline";
    public const string SaveFailed = "save_failed";
}
