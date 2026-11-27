using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Simulates an audit adapter that reports a failure by throwing (for example a database or
/// network error), or a request that is cancelled while the audit record is written.
/// </summary>
public sealed class ThrowingAuditWriter(Exception exception) : ISessionSecurityAuditWriter
{
    public int Attempts { get; private set; }

    public ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Attempts++;
        throw exception;
    }
}
