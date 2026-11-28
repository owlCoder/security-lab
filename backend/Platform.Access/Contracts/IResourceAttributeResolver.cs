using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IResourceAttributeResolver
{
    string ResourceType { get; }

    ValueTask<ResourceDescriptor?> ResolveAsync(
        ResourceRef resource,
        CancellationToken cancellationToken);

    ValueTask<ResourceDescriptor?> ResolveAsync(
        ResourceRef resource,
        SubjectRef subject,
        CancellationToken cancellationToken)
        => ResolveAsync(resource, cancellationToken);
}
