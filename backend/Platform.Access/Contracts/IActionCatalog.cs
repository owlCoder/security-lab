using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IActionCatalog
{
    bool Contains(string action);

    bool TryGet(string action, out ActionDefinition? definition);

    IReadOnlyCollection<ActionDefinition> Definitions { get; }
}
