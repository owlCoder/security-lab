using Microsoft.Extensions.Logging;
using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public static class AccessAuditEventIds
{
    public const string DeniedEventType = "access.denied";

    public static readonly EventId Lost = new(6102, "access.audit.lost");
}

public interface IAccessAuditWriter
{
    ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        DecisionTrace trace,
        CancellationToken cancellationToken = default) =>
        WriteAsync(request, decision, cancellationToken);
}
