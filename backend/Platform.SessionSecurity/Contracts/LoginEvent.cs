// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: one stored login event (written by the login flow, AUD-01). It stays inside the server;
/// the user receives a masked <see cref="LoginHistoryItem"/>.
/// </summary>
/// <param name="SourceAddress">Full source IP address; null when it is unknown.</param>
public sealed record LoginEvent(
    string SubjectId,
    DateTimeOffset OccurredAt,
    bool Succeeded,
    string? SourceAddress)
{
    // AUD-03 / AUD-19: the full source address must not appear in logs or test output.
    public override string ToString() =>
        $"LoginEvent {{ OccurredAt = {OccurredAt:O}, Succeeded = {Succeeded} }}";
}
