namespace Platform.Api.Modules.Assignments.Api;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Telo zahteva za ASN-06. Namerno sadrži SAMO novi rok: akter dolazi iz sesije, kurs iz same
/// dodele, a student i dodela iz rute. Eventualna polja kao <c>actorId</c> ili <c>courseId</c> u
/// telu se ignorišu. Rok je string da bi se zahtevala eksplicitna zona (<c>Z</c> ili <c>+hh:mm</c>).
/// </summary>
public sealed record ExtendDeadlineRequest(string? NewDueAtUtc);

public sealed record StartAttemptResponse(Guid AttemptId);

public sealed record ExtendDeadlineResponse(
    Guid AssignmentId,
    string StudentId,
    DateTimeOffset DueAtUtc,
    DateTimeOffset PreviousDueAtUtc,
    DateTimeOffset ExtendedAtUtc);

/// <summary>Bezbedan odgovor greške: samo stabilan kod, bez poruke izuzetka i internih detalja.</summary>
public sealed record AssignmentErrorResponse(string Code);

/// <summary>Rezultat API handlera, nezavisan od MVC-a radi testiranja.</summary>
public sealed record AssignmentApiResult(int StatusCode, object Body);

public static class AssignmentErrorCodes
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string AssignmentNotAvailable = "assignment_not_available";
    public const string AssignmentClosed = "assignment_closed";
    public const string AttemptLimitReached = "attempt_limit_reached";
    public const string InvalidRequestId = "invalid_request_id";
    public const string InvalidStudent = "invalid_student";
    public const string InvalidDeadline = "invalid_deadline";
    public const string DeadlineExtensionRejected = "deadline_extension_rejected";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}
