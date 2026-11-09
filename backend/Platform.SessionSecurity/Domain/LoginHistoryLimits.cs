// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-22: limits of a login history search (protection against abuse of the query, SEC-04).
/// These are the only place where the limits are defined.
/// </summary>
public static class LoginHistoryLimits
{
    /// <summary>Largest allowed time range of one search.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(90);

    /// <summary>Time range used when the caller does not give a start time.</summary>
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    public const int FirstPage = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;
}
