using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Composition;

public sealed class PostgresTransactionalAuditWriter(TimeProvider clock) : ITransactionalAuditWriter
{
    public async ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        try
        {
            if (transaction.Connection is not { } connection)
                return AuditWriteResult.Unavailable();
            // Passing an existing connection does not transfer ownership to EF.
            var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection).Options;
            await using var db = new AuditDbContext(options);
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var writer = new AuditWriter(new AuditDraftValidator(), new NoAmbientAuditContext(),
                new PostgresAuditEventStore(db), clock);
            return await writer.WriteAsync(draft, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return AuditWriteResult.Unavailable(); }
    }
}
