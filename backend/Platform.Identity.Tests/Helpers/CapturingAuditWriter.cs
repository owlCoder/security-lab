using System.Collections.Concurrent;
using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Helpers;

/// <summary>
/// Trusted writer that remembers every draft it receives and reports success, like SuccessfulAuditWriter.
/// It does not validate against the Audit catalog; catalog rules are tested in Platform.Audit.Tests.
/// </summary>
internal sealed class CapturingAuditWriter : ITrustedAuditWriter
{
    private readonly ConcurrentQueue<(AuditEventDraft Draft, AuditContext Context)> _written = new();

    public IReadOnlyList<(AuditEventDraft Draft, AuditContext Context)> Written => _written.ToArray();

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _written.Enqueue((draft, context));
        return ValueTask.FromResult(AuditWriteResult.Recorded(draft.EventId ?? Guid.NewGuid()));
    }
}
