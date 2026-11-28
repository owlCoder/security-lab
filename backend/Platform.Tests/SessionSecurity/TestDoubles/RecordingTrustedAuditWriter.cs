using System.Collections.Concurrent;
using Platform.Audit.Contracts;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Stands in for the Audit module (Team 12): keeps every draft and context it receives, then
/// returns the configured result or throws the configured exception. A cancelled token is
/// honoured before anything is recorded.
/// </summary>
public sealed class RecordingTrustedAuditWriter(AuditWriteResult? result = null, Exception? exception = null)
    : ITrustedAuditWriter
{
    public static readonly Guid RecordedEventId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly ConcurrentQueue<(AuditEventDraft Draft, AuditContext Context)> writes = new();

    public IReadOnlyList<(AuditEventDraft Draft, AuditContext Context)> Writes => writes.ToArray();

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        writes.Enqueue((draft, context));

        if (exception is not null)
        {
            throw exception;
        }

        return ValueTask.FromResult(result ?? AuditWriteResult.Recorded(RecordedEventId));
    }
}
