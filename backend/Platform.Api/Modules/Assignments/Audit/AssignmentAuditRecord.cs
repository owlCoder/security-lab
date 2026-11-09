using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Potpun zapis jedne radnje: akter, radnja, vreme, ishod i korelacioni ID (AUD-01, AUD-04),
/// plus identifikatori iz <see cref="AssignmentAuditDraft"/>. Gradi ga isključivo
/// <see cref="AssignmentAuditWriter"/>; pozivalac ne može da postavi aktera, vreme ni korelaciju.
/// Ne sadrži lozinke, tokene, kolačiće, ID sesije, telo zahteva ni odgovore studenta (AUD-03).
/// </summary>
public sealed record AssignmentAuditRecord(
    AuditActor Actor,
    string Action,
    DateTimeOffset OccurredAtUtc,
    AssignmentAuditOutcome Outcome,
    string CorrelationId,
    Guid AssignmentId,
    string? CourseId,
    string? StudentId,
    string? ReasonCode,
    DateTimeOffset? PreviousDueAtUtc,
    DateTimeOffset? NewDueAtUtc,
    Guid? AttemptId);
