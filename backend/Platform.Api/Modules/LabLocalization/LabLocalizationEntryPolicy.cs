using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.LabLocalization;

public sealed class LabLocalizationEntryPolicy : IAccessPolicy
{
    public string Id => LabLocalizationActions.Entry;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            request.Subject is { State: AccountState.Active }
            && !string.IsNullOrWhiteSpace(request.Subject.Id)
            && request.Resource is null);
}
