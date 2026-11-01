using Platform.Access.Contracts;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Catalog;

public sealed class InMemoryActionCatalog : IActionCatalog
{
    private readonly HashSet<string> actions = new(StringComparer.Ordinal);

    public void Register(string action)
    {
        if (!ActionId.IsValid(action))
        {
            throw new ArgumentException(
                "Action must match the lowercase dotted ActionId format and be at most 64 characters.",
                nameof(action));
        }

        if (!actions.Add(action))
        {
            throw new InvalidOperationException($"Action '{action}' is already registered.");
        }
    }

    public bool Contains(string action) =>
        ActionId.IsValid(action) && actions.Contains(action);
}
