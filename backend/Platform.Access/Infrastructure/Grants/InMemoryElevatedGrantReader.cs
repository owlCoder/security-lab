using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public sealed class InMemoryElevatedGrantReader : IElevatedGrantReader
{
    private readonly ElevatedGrant[] grants;

    public InMemoryElevatedGrantReader(IEnumerable<ElevatedGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        this.grants = grants.ToArray();
    }

    public ValueTask<IReadOnlyList<ElevatedGrant>> GetForHolderAsync(
        Guid holderId,
        string action,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ElevatedGrant> matching = grants
            .Where(grant => grant.HolderId == holderId
                && string.Equals(grant.Action, action, StringComparison.Ordinal))
            .ToArray();

        return ValueTask.FromResult(matching);
    }
}
