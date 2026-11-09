using Microsoft.Extensions.Logging;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Infrastructure.Common;

/// <summary>Stands in for the AUD log, writes privileged actions to the application log.</summary>
public sealed class LoggingAuditTrail : IAuditTrail
{
    private readonly ICurrentActor _actor;
    private readonly ICorrelationContext _correlation;
    private readonly TimeProvider _time;
    private readonly ILogger<LoggingAuditTrail> _logger;

    public LoggingAuditTrail(
        ICurrentActor actor,
        ICorrelationContext correlation,
        TimeProvider time,
        ILogger<LoggingAuditTrail> logger)
    {
        _actor = actor;
        _correlation = correlation;
        _time = time;
        _logger = logger;
    }

    public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Audit at {OccurredAt:O}: actor {ActorId}, action {Action}, resource {ResourceId}, outcome {Outcome}, correlation {CorrelationId}",
            _time.GetUtcNow().UtcDateTime, _actor.UserId, action, resourceId, outcome, _correlation.CorrelationId);

        return Task.CompletedTask;
    }
}
