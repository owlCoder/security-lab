using Microsoft.Extensions.Logging;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Auditing;

// Strukturisan zapis u log dok AUD timovi ne objave trajni audit.
// Ispisuje samo polja događaja; događaj nema polje za sadržaj.
public sealed class LoggerLabVisibilityAudit(ILogger<LoggerLabVisibilityAudit> logger) : ILabVisibilityAudit
{
    public ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        logger.LogInformation(
            "LabVisibility audit: action={Action} lab={LabId} actor={ActorId} outcome={Outcome} correlation={CorrelationId}",
            auditEvent.Action,
            auditEvent.LabId,
            auditEvent.ActorId ?? "anonymous",
            auditEvent.Outcome,
            auditEvent.CorrelationId);

        return ValueTask.CompletedTask;
    }
}
