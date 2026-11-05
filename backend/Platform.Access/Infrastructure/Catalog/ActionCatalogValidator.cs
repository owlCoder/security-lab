using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Catalog;

internal static class ActionCatalogValidator
{
    public static void Validate(ActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!ActionId.IsValid(definition.Id))
        {
            throw new ActionCatalogValidationException(
                $"Action '{definition.Id}' has an invalid ActionId format.");
        }

        if (definition.ResourceType is not null && !IsValidResourceType(definition.ResourceType))
        {
            throw new ActionCatalogValidationException(
                $"Action '{definition.Id}' has an invalid resource type.");
        }

        if (definition.Scope == ActionScope.Course && definition.ResourceType is null)
        {
            throw new ActionCatalogValidationException(
                $"Course action '{definition.Id}' must declare a resource type.");
        }

        if (definition.Scope != ActionScope.Course && definition.ResourceType is not null)
        {
            throw new ActionCatalogValidationException(
                $"Non-course action '{definition.Id}' cannot declare a resource type.");
        }
    }

    public static void ValidateCompleteness(IEnumerable<ActionDefinition> definitions)
    {
        var materialized = definitions.ToArray();
        var duplicateIds = materialized
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw new ActionCatalogValidationException(
                $"Duplicate action definitions: {string.Join(", ", duplicateIds)}.");
        }

        foreach (var definition in materialized)
        {
            Validate(definition);
        }
    }

    public static void ValidateCompleteness(
        IEnumerable<ActionDefinition> definitions,
        IReadOnlySet<string> requiredIds)
    {
        var materialized = definitions.ToArray();
        ValidateCompleteness(materialized);

        var actualIds = materialized
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var missingIds = requiredIds
            .Where(requiredId => !actualIds.Contains(requiredId))
            .ToArray();

        if (missingIds.Length > 0)
        {
            throw new ActionCatalogValidationException(
                $"Required actions are missing from the catalog: {string.Join(", ", missingIds)}.");
        }
    }

    private static bool IsValidResourceType(string value) =>
        value.Length is > 0 and <= 64 &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(character =>
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character == '-');
}
