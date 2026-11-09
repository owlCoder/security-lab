using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Api.Security.RevisionAuthorization;

public sealed class RevisionAccessAuditWriter(
    ITrustedAuditWriter auditWriter,
    IRevisionAuditContextProvider contextProvider) : IAccessAuditWriter
{
    public async ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var auditEvent = RevisionAuditEventFactory.CreateDeniedEvent(
            request,
            decision);

        if (auditEvent is null)
        {
            return;
        }

        var context = contextProvider.GetContext();

        var result = await auditWriter.WriteAsync(
            auditEvent,
            context,
            cancellationToken);

        if (!result.IsRecorded)
        {
            throw new InvalidOperationException(
                $"REV-05 audit write failed: {result.Status}");
        }
    }
}
