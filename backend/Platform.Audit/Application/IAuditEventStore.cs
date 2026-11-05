using Platform.Audit.Domain;

namespace Platform.Audit.Application;

// Internal integration proposal for S-06; this is not the public audit writer contract.
internal interface IAuditEventStore
{
    // In an existing transaction, the caller must commit before reporting success.
    Task<AuditAppendStatus> AppendAsync(AuditEvent record, CancellationToken cancellationToken = default);
    Task<AuditEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

internal enum AuditAppendStatus
{
    Appended,
    AlreadyPresent,
    Conflict
}
