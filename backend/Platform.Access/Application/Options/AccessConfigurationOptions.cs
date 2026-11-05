namespace Platform.Access.Application.Options;

public sealed class AccessConfigurationOptions
{
    public bool RequirePolicyCoverage { get; init; }

    public IReadOnlySet<string> ActionsWithoutPolicy { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
}
