using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IResourceOwnershipResolver
{
    ValueTask<Guid?> ResolveOwnerIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default);
}
