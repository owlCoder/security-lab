using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>Simulates an unavailable audit store: every write fails.</summary>
public sealed class FailingAuditWriter : ISessionSecurityAuditWriter
{
    public int Attempts { get; private set; }

    public ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Attempts++;
        return ValueTask.FromResult(false);
    }
}
