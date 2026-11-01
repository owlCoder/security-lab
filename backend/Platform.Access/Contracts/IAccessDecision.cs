using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IAccessDecision
{
    ValueTask<AccessDecision> DecideAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default);
}
