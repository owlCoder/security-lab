using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Policies;

// #185: politika ulazne radnje lab.visibility.entry (isti obrazac kao revision.entry, #192).
// Propušta samo aktivan nalog i zahtev bez resursa. Ne zna ništa o kursu ni laboratoriji:
// to proveravaju StudentViewPolicy i LabPreviewPolicy kroz IAccessDecision u servisima.
public sealed class LabVisibilityEntryPolicy : IAccessPolicy
{
    public string Id => LabVisibilityActions.Entry;
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            request.Subject is { State: AccountState.Active }
            && !string.IsNullOrWhiteSpace(request.Subject.Id)
            && request.Resource is null);
}
