using System.Collections.Concurrent;
using System.Collections.Frozen;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Resources;


public sealed class InMemoryResourceAttributeResolver : IResourceAttributeResolver
{
    private readonly ConcurrentDictionary<string, ResourceDescriptor> descriptors =
        new(StringComparer.Ordinal);

    public InMemoryResourceAttributeResolver(string resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ArgumentException("Resource type must not be empty.", nameof(resourceType));
        }

        ResourceType = resourceType;
    }

    public string ResourceType { get; }

    public void Set(ResourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(descriptor.Attributes);

        if (!string.Equals(descriptor.Type, ResourceType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Descriptor type '{descriptor.Type}' does not match resolver type '{ResourceType}'.",
                nameof(descriptor));
        }

        if (string.IsNullOrEmpty(descriptor.Id))
        {
            throw new ArgumentException("Descriptor ID must not be empty.", nameof(descriptor));
        }

        
        descriptors[descriptor.Id] = descriptor with
        {
            Attributes = descriptor.Attributes.ToFrozenDictionary(StringComparer.Ordinal)
        };
    }

    public ValueTask<ResourceDescriptor?> ResolveAsync(
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(resource.Type, ResourceType, StringComparison.Ordinal) || resource.Id is null)
        {
            return ValueTask.FromResult<ResourceDescriptor?>(null);
        }

        return ValueTask.FromResult<ResourceDescriptor?>(
            descriptors.TryGetValue(resource.Id, out var descriptor) ? descriptor : null);
    }
}