using Platform.Access.Application.Options;
using Platform.Access.Contracts;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Application.Services;

public static class AccessConfigurationValidator
{
    public static IReadOnlyList<string> Validate(
        IActionCatalog catalog,
        IEnumerable<string> policyActions,
        AccessConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(policyActions);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var registered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var action in policyActions)
        {
            if (!ActionId.IsValid(action))
            {
                errors.Add($"A policy is registered under an invalid ActionId (length {action?.Length ?? 0}).");
                continue;
            }

            if (!catalog.Contains(action))
            {
                errors.Add($"Policy registered for action '{action}' which is not in the action catalog.");
            }

            registered.Add(action);
        }

        foreach (var definition in catalog.Definitions)
        {
            if (!ActionId.IsValid(definition.Id))
            {
                errors.Add($"Catalog contains an invalid ActionId (length {definition.Id?.Length ?? 0}).");
            }
        }

        foreach (var exempt in options.ActionsWithoutPolicy)
        {
            if (!catalog.Contains(exempt))
            {
                errors.Add($"Policy exemption declared for unknown action '{Describe(exempt)}'.");
            }
            else if (registered.Contains(exempt))
            {
                errors.Add($"Action '{exempt}' is declared policy-exempt but also has a policy.");
            }
        }

        var uncovered = catalog.Definitions
            .Select(definition => definition.Id)
            .Where(id => !registered.Contains(id) && !options.ActionsWithoutPolicy.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (options.RequirePolicyCoverage && uncovered.Length > 0)
        {
            errors.Add($"Actions without policy or explicit exemption: {string.Join(", ", uncovered)}.");
        }

        if (errors.Count > 0)
        {
            throw new AccessConfigurationException(string.Join(" ", errors));
        }

        return uncovered;
    }

    private static string Describe(string value) =>
        ActionId.IsValid(value) ? value : $"<invalid, length {value.Length}>";
}
