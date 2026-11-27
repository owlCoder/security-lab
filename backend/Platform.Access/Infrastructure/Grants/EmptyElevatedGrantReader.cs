using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public sealed class EmptyElevatedGrantReader : IElevatedGrantReader
{
    public ValueTask<IReadOnlyList<ElevatedGrant>> GetForHolderAsync(
        Guid holderId,
        string action,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>([]);
}
