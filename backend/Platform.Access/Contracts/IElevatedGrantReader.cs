using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IElevatedGrantReader
{
    ValueTask<IReadOnlyList<ElevatedGrant>> GetForHolderAsync(
        Guid holderId,
        string action,
        CancellationToken cancellationToken);
}
