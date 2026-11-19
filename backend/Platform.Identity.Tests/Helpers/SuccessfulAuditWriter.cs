using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Helpers;

/// <summary>
/// IDN-only HTTP fixtures do not exercise Audit persistence. They still keep the
/// production fail-closed contract by explicitly supplying a successful trusted writer.
/// Cross-module PostgreSQL persistence is covered by IdentityAuditPostgresIntegrationTests.
/// </summary>
internal sealed class SuccessfulAuditWriter : ITrustedAuditWriter
{
    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AuditWriteResult.Recorded(draft.EventId ?? Guid.NewGuid()));
    }
}
