namespace Platform.Access.Application.Options;

public sealed class AccessDecisionOptions
{
    public TimeSpan PolicyTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan AuditTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public void Validate()
    {
        EnsureFinitePositive(PolicyTimeout, nameof(PolicyTimeout));
        EnsureFinitePositive(AuditTimeout, nameof(AuditTimeout));
    }

    private static void EnsureFinitePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(name, "Timeout must be a finite, positive duration.");
        }
    }
}
