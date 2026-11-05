namespace Platform.Access.Infrastructure.Auditing;

internal interface IAccessAuditSink
{
    ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default);
}
