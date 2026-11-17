// IDN-11 IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
