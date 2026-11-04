using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

public sealed class LoggerAuditWriter(ILogger<LoggerAuditWriter> logger) : IAuditWriter
{
    public void Write(AuditEvent e) =>
        logger.LogInformation(
            "AUDIT {Time} actor={Actor} action={Action} target={Target} outcome={Outcome} corr={Corr} detail={Detail}",
            e.Time, e.ActorId, e.Action, e.Target, e.Outcome, e.CorrelationId, e.Detail);
}
