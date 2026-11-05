using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IResourceDescriptorResolver
{
    ValueTask<Guid?> ResolveCourseIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default);
}
