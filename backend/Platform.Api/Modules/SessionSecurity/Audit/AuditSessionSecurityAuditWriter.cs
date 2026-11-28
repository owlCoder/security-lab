using System.Diagnostics.CodeAnalysis;
using Platform.Audit.Contracts;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Audit;

/// <summary>
/// IDN-11 / AUD-01: writes session rotation records to the Audit module (Team 12) as
/// <c>session.rotated</c>, with the user as actor and their account as resource. Returns false
/// when the entry cannot be mapped or the record is not stored, so the rotation fails closed
/// (AUD-10). The draft holds only the trigger and the reason code, never a token, a token hash,
/// a session handle or a chain ID (AUD-03). The Audit module sets the event time.
/// </summary>
internal sealed class AuditSessionSecurityAuditWriter(
    ITrustedAuditWriter writer,
    ILogger<AuditSessionSecurityAuditWriter> logger) : ISessionSecurityAuditWriter
{
    public async ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreate(entry, out var draft, out var context))
        {
            logger.LogError("Session security audit entry could not be mapped to the audit catalog");
            return false;
        }

        try
        {
            var result = await writer.WriteAsync(draft, context, cancellationToken);
            if (result.IsRecorded)
            {
                return true;
            }

            logger.LogError(
                "Audit write failed for {EventType} with status {Status} ({RejectionCode}); correlation {CorrelationId}",
                draft.EventType,
                result.Status,
                result.RejectionCode,
                context.CorrelationId);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // AUD-10: any storage failure means the rotation is not audited. The caller treats
            // false as a failed rotation, so the exception is not passed on; cancellation is.
            logger.LogError(
                "Audit writer failed unexpectedly for {EventType}; correlation {CorrelationId}",
                draft.EventType,
                context.CorrelationId);
            return false;
        }
    }

    private static bool TryCreate(
        SessionSecurityAuditEntry? entry,
        [NotNullWhen(true)] out AuditEventDraft? draft,
        [NotNullWhen(true)] out AuditContext? context)
    {
        draft = null;
        context = null;

        // D1: the subject is the user ID in "D" format; anything else has no audit actor.
        if (entry is null
            || entry.EventType != SessionSecurityAuditEvents.SessionRotated
            || !Guid.TryParseExact(entry.SubjectId, "D", out var userId)
            || userId == Guid.Empty
            || !SessionRotationAuditMapping.TryMapTrigger(entry.Trigger, out var trigger)
            || !SessionRotationAuditMapping.TryMapOutcome(entry.Outcome, out var outcome, out var reasonCode))
        {
            return false;
        }

        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SessionRotationAuditMapping.TriggerDetail] = trigger,
        };
        if (reasonCode is not null)
        {
            details[SessionRotationAuditMapping.ReasonCodeDetail] = reasonCode;
        }

        draft = new AuditEventDraft(
            AuditEventTypes.SessionRotated,
            outcome,
            Resource: new AuditResourceRef(AuditResourceTypes.Account, userId.ToString("D")),
            Details: details);
        context = new AuditContext(AuditActor.ForUser(userId), entry.CorrelationId);
        return true;
    }
}
