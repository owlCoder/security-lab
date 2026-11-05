using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Resources;

public sealed class EmptyResourceDescriptorResolver : IResourceDescriptorResolver
{
    public ValueTask<Guid?> ResolveCourseIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Guid?>(null);
    }
}
