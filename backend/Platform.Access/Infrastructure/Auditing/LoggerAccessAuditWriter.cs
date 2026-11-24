using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed partial class LoggerAccessAuditWriter(ILogger<LoggerAccessAuditWriter> logger) : IAccessAuditWriter
{
    private const string Rejected = "invalid";

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (decision.Outcome != AccessOutcome.Allow)
        {
            LogDenied(
                logger,
                ActionId.IsValid(request.Action) ? request.Action : Rejected,
                decision.DenialReason,
                CorrelationIdPattern().IsMatch(decision.CorrelationId) ? decision.CorrelationId : Rejected);
        }

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(
        EventId = 6101,
        EventName = "access.denied",
        Level = LogLevel.Warning,
        Message = "access.denied action={Action} denialReason={DenialReason} correlationId={CorrelationId}")]
    private static partial void LogDenied(
        ILogger logger, string action, AccessDenialReason denialReason, string correlationId);
}
