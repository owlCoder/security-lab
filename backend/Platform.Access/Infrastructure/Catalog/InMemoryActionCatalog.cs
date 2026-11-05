using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.ValueObjects;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Catalog;

public sealed class InMemoryActionCatalog : IActionCatalog
{
    private readonly Dictionary<string, ActionDefinition> actions =
        new(StringComparer.Ordinal);

    public InMemoryActionCatalog()
    {
        ActionCatalogValidator.ValidateCompleteness(AccessActions.All, AccessActions.RequiredIds);

        foreach (var action in AccessActions.All)
        {
            Register(action);
        }
    }

    public IReadOnlyCollection<ActionDefinition> Definitions => actions.Values;

    public void Register(string action)
    {
        Register(new ActionDefinition(action, ActionScope.Authenticated, null, false));
    }

    public void Register(ActionDefinition definition)
    {
        ActionCatalogValidator.Validate(definition);

        if (!actions.TryAdd(definition.Id, definition))
        {
            throw new InvalidOperationException($"Action '{definition.Id}' is already registered.");
        }
    }

    public bool Contains(string action) =>
        TryGet(action, out _);

    public bool TryGet(string action, out ActionDefinition? definition)
    {
        if (!ActionId.IsValid(action))
        {
            definition = null;
            return false;
        }

        return actions.TryGetValue(action, out definition);
    }
}
