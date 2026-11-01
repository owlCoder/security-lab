using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Auditing;

public sealed class NullAccessAuditWriter : IAccessAuditWriter
{
    public ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
