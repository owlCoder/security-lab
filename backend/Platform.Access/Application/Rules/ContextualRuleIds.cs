using Platform.Access.Application.Services;
using Platform.Access.Domain.Models;

namespace Platform.Access.Application.Rules;

public static class ContextualRuleIds
{
    private const string RequiredPrefix = "rule.";

    private static readonly HashSet<string> GuardIds =
        new(StringComparer.Ordinal)
        {
            "resource.present",
            "action.relations",
            "resource.course",
            "resource.type",
            "subject.id",
            "resource.owner",
            "ownership.owner",
            "grant.holder",
            "course.scope",
            "membership.active",
            "relation.allowed"
        };

    public static void Validate(RuleSet<EvaluationSnapshot> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules.Requirements.Concat(rules.Prohibitions))
        {
            if (!rule.Id.StartsWith(RequiredPrefix, StringComparison.Ordinal))
            {
                throw new AccessConfigurationException(
                    $"Contextual rule ID '{rule.Id}' must start with '{RequiredPrefix}'.");
            }

            if (GuardIds.Contains(rule.Id))
            {
                throw new AccessConfigurationException(
                    $"Contextual rule ID '{rule.Id}' conflicts with a G7 guard ID.");
            }
        }
    }
}