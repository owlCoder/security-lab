using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.AccessPolicies;

internal sealed class PublicHealthReadPolicy : IAccessPolicy
{
    public string Id => "public.health.read";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Resource is null);
}
