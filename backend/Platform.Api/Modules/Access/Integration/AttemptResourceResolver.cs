using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Access.Integration;

public sealed class AttemptResourceResolver(IResourceDescriptorResolver inner, IServiceScopeFactory scopes)
    : IResourceDescriptorResolver, IResourceOwnershipResolver
{
    public async ValueTask<Guid?> ResolveCourseIdAsync(ResourceRef resource, CancellationToken ct = default)
        => resource.Type == "attempt" ? (await Snapshot(resource, ct))?.CourseId : await inner.ResolveCourseIdAsync(resource, ct);
    public async ValueTask<Guid?> ResolveOwnerIdAsync(ResourceRef resource, CancellationToken ct = default)
        => resource.Type == "attempt" ? (await Snapshot(resource, ct))?.OwnerId :
            inner is IResourceOwnershipResolver owners ? await owners.ResolveOwnerIdAsync(resource, ct) : null;
    private async Task<AttemptSnapshot?> Snapshot(ResourceRef resource, CancellationToken ct)
    {
        if (!Guid.TryParse(resource.Id, out var id) || id == Guid.Empty) return null;
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<HttpAttemptStore>().FindAttemptAsync(id, ct);
        return result.Status == PortStatus.Ok ? result.Value : null;
    }
    public sealed class Original(IResourceDescriptorResolver value, bool ownsValue) : IDisposable
    {
        public IResourceDescriptorResolver Value => value;
        public void Dispose() { if (ownsValue) (Value as IDisposable)?.Dispose(); }
    }
}
