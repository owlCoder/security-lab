
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public sealed class EmptyElevatedGrantReader : IElevatedGrantReader
{
    public ValueTask<IReadOnlyList<ElevatedGrant>> GetForHolderAsync(
        Guid holderId,
        string action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>(
            Array.Empty<ElevatedGrant>());
    }
}
