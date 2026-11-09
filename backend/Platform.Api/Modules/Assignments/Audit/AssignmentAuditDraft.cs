namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5: audit i API)

/// <summary>
/// Radnje Assignments modula koje se beleže. Format je isti kao <c>ActionId</c> u Platform.Access
/// (<c>^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$</c>), pa se ista vrednost može poslati i kao detalj
/// <c>action</c> u postojećem događaju <c>access.denied</c>.
/// </summary>
public static class AssignmentAuditActions
{
    /// <summary>ASN-04: student započinje pokušaj.</summary>
    public const string StartAttempt = "assignment.attempt.start";

    /// <summary>ASN-06: nastavnik produžava rok jednom studentu.</summary>
    public const string ExtendDeadline = "assignment.deadline.extend";

    public static bool IsKnown(string? action) =>
        action is StartAttempt or ExtendDeadline;
}

public enum AssignmentAuditOutcome
{
    /// <summary>Radnja je izvršena i sačuvana.</summary>
    Success = 1,

    /// <summary>Radnju je odbila provera prava.</summary>
    Denied = 2,

    /// <summary>Radnja je bila dozvoljena, ali je odbijena validacijom ili nije uspela.</summary>
    Failure = 3
}

/// <summary>
/// ŠTA se desilo. Namerno nema aktera, vremena ni korelacionog ID-a: njih dodaje
/// <see cref="IAssignmentAuditWriter"/> iz pouzdanog serverskog konteksta (sesija, middleware, sat).
/// Nema slobodnog teksta: samo identifikatori, vremena roka i fiksni kod razloga.
/// </summary>
public sealed record AssignmentAuditDraft(
    string Action,
    AssignmentAuditOutcome Outcome,
    Guid AssignmentId,
    string? CourseId = null,
    string? StudentId = null,
    string? ReasonCode = null,
    DateTimeOffset? PreviousDueAtUtc = null,
    DateTimeOffset? NewDueAtUtc = null,
    Guid? AttemptId = null);

/// <summary>
/// Kodovi razloga za ASN-04. ASN-06 koristi <c>DeadlineExtensionReasonCodes</c> iz Platform.Assignments.
/// Format kao <c>reasonCode</c> u Audit katalogu: <c>^[a-z][a-z0-9_]{0,31}$</c>.
/// </summary>
public static class StartAttemptReasonCodes
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string NotAvailable = "not_available";
    public const string AttemptLimitReached = "attempt_limit_reached";
    public const string Unavailable = "unavailable";
}
