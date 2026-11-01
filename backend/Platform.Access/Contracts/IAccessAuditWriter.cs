using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IAccessAuditWriter
{
    ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default);
}
