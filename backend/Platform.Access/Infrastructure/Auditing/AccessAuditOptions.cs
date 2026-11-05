using Platform.Access.Domain.Constants;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed class AccessAuditOptions
{
    public static IReadOnlySet<string> DefaultPrivilegedActions { get; } =
        new HashSet<string>(
            AccessActions.All
                .Where(action => action.IsPrivileged)
                .Select(action => action.Id),
            StringComparer.Ordinal);

    public IReadOnlySet<string> PrivilegedActions { get; init; } = DefaultPrivilegedActions;

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan DenyAggregationWindow { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxValueLength { get; init; } = 64;

    public int MaxAggregationKeys { get; init; } = 1024;
}
