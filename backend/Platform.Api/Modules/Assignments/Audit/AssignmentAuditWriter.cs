using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Adapter Assignments → Audit modul (tim 12). Redosled:
/// 1. očisti nacrt (allowlist), 2. uzme aktera i korelacioni ID iz pouzdanog konteksta i vreme iz
/// <see cref="TimeProvider"/>, 3. upiše kroz <see cref="ITrustedAuditWriter"/>, 4. ako tip još nije u
/// katalogu, odbijanje upiše kao <c>access.denied</c>, 5. kada zapis nije sačuvan, podigne tehnički
/// alarm (samo radnja, ishod, status i korelacioni ID; bez detalja).
/// </summary>
public sealed class AssignmentAuditWriter(
    ITrustedAuditWriter auditWriter,
    IAssignmentAuditContextProvider contextProvider,
    TimeProvider timeProvider,
    ILogger<AssignmentAuditWriter> logger) : IAssignmentAuditWriter
{
    public static readonly EventId RecordEvent = new(41501, "AssignmentAuditRecord");
    public static readonly EventId AlarmEvent = new(41502, "AssignmentAuditNotRecorded");

    public async ValueTask<bool> WriteAsync(
        AssignmentAuditDraft draft,
        CancellationToken cancellationToken = default)
    {
        AssignmentAuditDraft safe;
        AuditContext context;

        try
        {
            safe = AssignmentAuditDraftMapper.Sanitize(draft);
            context = contextProvider.GetContext();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Greška u našem kodu; vrednosti iz nacrta se ne prepisuju u log.
            logger.LogError(AlarmEvent, "Assignment audit draft rejected before writing ({ExceptionType}).",
                exception.GetType().Name);
            return false;
        }

        var record = new AssignmentAuditRecord(
            context.Actor,
            safe.Action,
            timeProvider.GetUtcNow(),
            safe.Outcome,
            context.CorrelationId,
            safe.AssignmentId,
            safe.CourseId,
            safe.StudentId,
            safe.ReasonCode,
            safe.PreviousDueAtUtc,
            safe.NewDueAtUtc,
            safe.AttemptId);

        LogRecord(record);

        AuditWriteResult result;
        try
        {
            result = await auditWriter.WriteAsync(
                AssignmentAuditDraftMapper.ToPlatformDraft(safe), context, cancellationToken);

            // Dok tim 12 ne doda assignment.* u katalog, odbijanje ne sme da ostane bez traga.
            if (!result.IsRecorded
                && result.RejectionCode == AuditRejectionCode.UnknownEventType
                && safe.Outcome == AssignmentAuditOutcome.Denied)
            {
                result = await auditWriter.WriteAsync(
                    AssignmentAuditDraftMapper.ToAccessDeniedDraft(safe), context, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(AlarmEvent,
                "Assignment audit not recorded: {Action} {Outcome} failed with {ExceptionType}. CorrelationId: {CorrelationId}",
                record.Action, record.Outcome, exception.GetType().Name, record.CorrelationId);
            return false;
        }

        if (!result.IsRecorded)
        {
            logger.LogWarning(AlarmEvent,
                "Assignment audit not recorded: {Action} {Outcome} status {Status} ({RejectionCode}). CorrelationId: {CorrelationId}",
                record.Action, record.Outcome, result.Status, result.RejectionCode, record.CorrelationId);
        }

        return result.IsRecorded;
    }

    // Strukturisana linija samo sa dozvoljenim poljima (AUD-03). Nije zamena za audit zapis;
    // služi za praćenje zahteva (AUD-04) i za demonstraciju.
    private void LogRecord(AssignmentAuditRecord record) =>
        logger.LogInformation(RecordEvent,
            "Assignment audit {Action} {Outcome} actor {ActorKind}:{ActorId} assignment {AssignmentId} course {CourseId} student {StudentId} reason {ReasonCode} at {OccurredAtUtc:O}. CorrelationId: {CorrelationId}",
            record.Action,
            record.Outcome,
            record.Actor.Kind,
            record.Actor.Id,
            record.AssignmentId,
            record.CourseId,
            record.StudentId,
            record.ReasonCode,
            record.OccurredAtUtc,
            record.CorrelationId);
}
