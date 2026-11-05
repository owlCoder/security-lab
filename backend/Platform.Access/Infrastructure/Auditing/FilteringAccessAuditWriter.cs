using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed class FilteringAccessAuditWriter(
    AccessAuditRecordFactory factory,
    IAccessAuditSink sink,
    AccessAuditOptions options) : IAccessAuditWriter
{
    public ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        return ShouldAudit(request, decision)
            ? sink.WriteAsync(factory.Create(request, decision), cancellationToken)
            : ValueTask.CompletedTask;
    }

    private bool ShouldAudit(AccessRequest request, AccessDecision decision) =>
        decision.Outcome != AccessOutcome.Allow || options.PrivilegedActions.Contains(request.Action);
}
