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
    private readonly object registrationLock = new();
    private bool isFrozen;
    private IReadOnlyCollection<ActionDefinition>? frozenDefinitions;

    public InMemoryActionCatalog(
        IEnumerable<ActionDefinition>? additionalDefinitions = null,
        bool freezeAfterConstruction = false)
    {
        var definitions = AccessActions.All
            .Concat(additionalDefinitions ?? [])
            .ToArray();
        ActionCatalogValidator.ValidateCompleteness(definitions, AccessActions.RequiredIds);

        foreach (var action in definitions)
        {
            Register(action);
        }

        if (freezeAfterConstruction)
        {
            Freeze();
        }
    }

    public IReadOnlyCollection<ActionDefinition> Definitions
    {
        get
        {
            lock (registrationLock)
            {
                return frozenDefinitions ?? actions.Values.ToArray();
            }
        }
    }

    public void Register(string action)
    {
        Register(new ActionDefinition(action, ActionScope.Authenticated, null, false));
    }

    public void Register(ActionDefinition definition)
    {
        ActionCatalogValidator.Validate(definition);

        lock (registrationLock)
        {
            if (isFrozen)
            {
                throw new InvalidOperationException(
                    "Access actions cannot be registered after application startup.");
            }

            if (!actions.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Action '{definition.Id}' is already registered.");
            }
        }
    }

    internal void Freeze()
    {
        lock (registrationLock)
        {
            frozenDefinitions = Array.AsReadOnly(actions.Values.ToArray());
            isFrozen = true;
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

        lock (registrationLock)
        {
            return actions.TryGetValue(action, out definition);
        }
    }
}
