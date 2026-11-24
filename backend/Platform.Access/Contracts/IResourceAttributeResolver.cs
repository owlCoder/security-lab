using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IResourceAttributeResolver
{
    string ResourceType { get; }

    ValueTask<ResourceDescriptor?> ResolveAsync(
        ResourceRef resource,
        CancellationToken cancellationToken);
}
