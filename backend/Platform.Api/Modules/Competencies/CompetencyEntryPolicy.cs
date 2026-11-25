using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Competencies;

// Entry requires a confirmed session. Concrete laboratory read/edit permissions
// remain in CompetencyLinkService, including its non-enumerating 404 responses.
public sealed class CompetencyEntryPolicy : IAccessPolicy
{
    public string Id => CompetencyLinkActions.Entry;
    public string Version => "1";

    public ValueTask<bool> EvaluateAsync(AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Action == CompetencyLinkActions.Entry &&
            request.Resource is null && request.Subject is { State: AccountState.Active } subject &&
            Guid.TryParse(subject.Id, out var userId) && userId != Guid.Empty);
}
