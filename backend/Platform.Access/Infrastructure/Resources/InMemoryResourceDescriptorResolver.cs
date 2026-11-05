using System.Collections.Concurrent;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Resources;

public sealed class InMemoryResourceDescriptorResolver :
    IResourceDescriptorResolver,
    IResourceOwnershipResolver
{
    private readonly ConcurrentDictionary<(string Type, string Id), Guid> resourceCourses = new();
    private readonly ConcurrentDictionary<(string Type, string Id), Guid> resourceOwners = new();

    public void SetCourse(ResourceRef resource, Guid courseId)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resourceCourses[(resource.Type, resource.Id)] = courseId;
    }

    public bool Remove(ResourceRef resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resourceOwners.TryRemove((resource.Type, resource.Id), out _);
        return resourceCourses.TryRemove((resource.Type, resource.Id), out _);
    }

    public void SetOwner(ResourceRef resource, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner ID must not be empty.", nameof(ownerId));
        resourceOwners[(resource.Type, resource.Id)] = ownerId;
    }

    public ValueTask<Guid?> ResolveCourseIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Guid?>(
            resourceCourses.TryGetValue((resource.Type, resource.Id), out var courseId)
                ? courseId
                : null);
    }

    public ValueTask<Guid?> ResolveOwnerIdAsync(
        ResourceRef resource,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Guid?>(
            resourceOwners.TryGetValue((resource.Type, resource.Id), out var ownerId)
                ? ownerId
                : null);
    }
}
