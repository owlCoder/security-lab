using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IOutcomeAccessPolicy : IAccessPolicy
{
    ValueTask<PolicyResult> EvaluateOutcomeAsync(
        AccessRequest request,
        CancellationToken cancellationToken);
}
