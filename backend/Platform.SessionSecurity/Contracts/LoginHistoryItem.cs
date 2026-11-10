// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: one entry of the personal login history as returned to the user. It has no user ID,
/// token, session ID or failure reason, and the source address is masked (AUD-03).
/// </summary>
public sealed record LoginHistoryItem(
    DateTimeOffset OccurredAt,
    bool Succeeded,
    string MaskedSourceAddress);
