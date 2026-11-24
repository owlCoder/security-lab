namespace Platform.Access.Tests.Support;

public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private long utcTicks = utcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);

    public void SetUtcNow(DateTimeOffset value) =>
        Interlocked.Exchange(ref utcTicks, value.UtcTicks);

    public void Advance(TimeSpan amount) =>
        Interlocked.Add(ref utcTicks, amount.Ticks);
}
