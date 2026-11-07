using Microsoft.Extensions.Logging;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Defaults;

/// <summary>Stands in for the AUD log, writes denials to the application log.</summary>
public sealed class LoggingVisibilityAuditWriter : IVisibilityAuditWriter
{
    private readonly ILogger<LoggingVisibilityAuditWriter> _logger;

    public LoggingVisibilityAuditWriter(ILogger<LoggingVisibilityAuditWriter> logger)
    {
        _logger = logger;
    }

    public Task WriteDenialAsync(VisibilityDenial denial, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Visibility denied at {OccurredAt:O}: actor {ActorId}, course {CourseId}, material {MaterialType}/{MaterialId}, action {Action}, reason {Reason}, correlation {CorrelationId}",
            denial.OccurredAt, denial.ActorId, denial.CourseId, denial.Material?.Type, denial.Material?.MaterialId,
            denial.Action, denial.Reason, denial.CorrelationId);

        return Task.CompletedTask;
    }
}
