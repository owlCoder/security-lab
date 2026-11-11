using System.Collections.Concurrent;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>Accepts every audit entry and keeps it for assertions.</summary>
public sealed class RecordingAuditWriter : ISessionSecurityAuditWriter
{
    private readonly ConcurrentQueue<SessionSecurityAuditEntry> entries = new();

    public IReadOnlyList<SessionSecurityAuditEntry> Entries => entries.ToArray();

    public ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        entries.Enqueue(entry);
        return ValueTask.FromResult(true);
    }
}
