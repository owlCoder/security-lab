// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-22: a login history filter after validation. All values are within
/// <see cref="LoginHistoryLimits"/>, the times are in UTC and both bounds are inclusive.
/// </summary>
public sealed record LoginHistorySearchWindow(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    LoginOutcomeFilter? Outcome,
    int Page,
    int PageSize,
    int Skip);
