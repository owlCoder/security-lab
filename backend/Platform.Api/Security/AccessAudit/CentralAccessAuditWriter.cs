using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Api.Security.AccessAudit;

public sealed class CentralAccessAuditWriter(
    IAccessDeniedAuditMapper mapper,
    IServiceScopeFactory scopes,
    ILogger<CentralAccessAuditWriter> logger) : IAccessAuditWriter
{
    public async ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (decision.Outcome == AccessOutcome.Allow)
        {
            return;
        }

        if (request.Subject is { } subject
            && (!Guid.TryParse(subject.Id, out var userId) || userId == Guid.Empty))
        {
            LogAuditLost("invalid_subject_id", decision.CorrelationId);
            return;
        }

        var draft = mapper.CreateDraft(request, decision);
        var context = mapper.CreateContext(request, decision);

        await using var scope = scopes.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();
        var result = await writer.WriteAsync(draft, context, cancellationToken);

        if (!result.IsRecorded)
        {
            LogAuditLost(result.Status.ToString(), decision.CorrelationId);
        }
    }

    private void LogAuditLost(string status, string correlationId) =>
        logger.LogError(
            AccessAuditEventIds.Lost,
            "Access denial audit was not saved. EventType: {EventType}, Status: {Status}, CorrelationId: {CorrelationId}",
            AccessAuditEventIds.DeniedEventType,
            status,
            correlationId);
}
