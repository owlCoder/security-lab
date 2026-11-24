using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Laboratories.Application;

internal sealed class CatalogReadPolicy : IAccessPolicy
{
    public const string ActionId = "catalog.read";

    public string Id => ActionId;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == ActionId && request.Resource is null &&
            request.Subject is { State: AccountState.Active } &&
            Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty);
}
