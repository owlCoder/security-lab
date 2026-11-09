using System.Data.Common;

namespace Platform.Audit.Contracts;

/// <summary>
/// Appends in the caller's PostgreSQL transaction. The caller must commit that
/// transaction before reporting success. The audit schema must be in the same database.
/// </summary>
public interface ITransactionalAuditWriter
{
    ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
        DbTransaction transaction, CancellationToken cancellationToken = default);
}
