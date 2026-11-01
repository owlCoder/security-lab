using Platform.Access.Domain.Models;

namespace Platform.Access.Contracts;

public interface IAccessPolicy
{
    string Id { get; }
    string Version { get; }

    ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default);
}
