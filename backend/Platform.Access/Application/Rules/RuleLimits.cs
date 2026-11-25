using Platform.Access.Application.Services;

namespace Platform.Access.Application.Rules;

public sealed record RuleLimits(int MaxRules = RuleLimits.DefaultMaxRules, int MaxDepth = 8, int MaxNodes = 64)
{
    public const int DefaultMaxRules = 24;

    public const int HardMaxRules = 32;

    public static RuleLimits Default { get; } = new();

    public void Validate()
    {
        if (MaxRules < 1 || MaxRules > HardMaxRules || MaxDepth < 1 || MaxNodes < 1)
        {
            throw new AccessConfigurationException($"Rule limits must be positive and MaxRules at most {HardMaxRules}.");
        }
    }
}
