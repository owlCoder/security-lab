using Platform.SessionSecurity.Contracts;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Synthetic login events for IDN-22 tests. Only documentation IP ranges (RFC 5737, RFC 3849)
/// and the synthetic subjects "user-a" and "user-b" are used.
/// </summary>
internal static class SyntheticLoginEvents
{
    public const string UserA = "user-a";
    public const string UserB = "user-b";

    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Events of user A, newest first. The last two are outside the default 30-day range.</summary>
    public static IReadOnlyList<LoginEvent> ForUserA() =>
    [
        new(UserA, Now.AddHours(-1), Succeeded: true, "203.0.113.45"),
        new(UserA, Now.AddDays(-2), Succeeded: false, "198.51.100.7"),
        new(UserA, Now.AddDays(-10), Succeeded: true, "2001:db8:85a3:8d3:1319:8a2e:370:7348"),
        new(UserA, Now.AddDays(-40), Succeeded: true, "192.0.2.10"),
        new(UserA, Now.AddDays(-100), Succeeded: false, "203.0.113.99"),
    ];

    /// <summary>Events of user B in the same period as the events of user A.</summary>
    public static IReadOnlyList<LoginEvent> ForUserB() =>
    [
        new(UserB, Now.AddHours(-3), Succeeded: true, "198.51.100.200"),
        new(UserB, Now.AddDays(-2).AddMinutes(-5), Succeeded: false, "192.0.2.77"),
        new(UserB, Now.AddDays(-5), Succeeded: true, "2001:db8::1"),
    ];

    public static IReadOnlyList<LoginEvent> ForBothUsers() => [.. ForUserA(), .. ForUserB()];
}
