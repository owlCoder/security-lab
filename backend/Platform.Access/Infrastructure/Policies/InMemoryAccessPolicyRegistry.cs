using Platform.Access.Contracts;

namespace Platform.Access.Infrastructure.Policies;

public sealed class InMemoryAccessPolicyRegistry : IAccessPolicyRegistry
{
    private readonly Dictionary<string, IAccessPolicy> policies =
        new(StringComparer.Ordinal);

    public void Register(string action, IAccessPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policies.TryAdd(action, policy))
        {
            throw new InvalidOperationException($"Policy for '{action}' is already registered.");
        }
    }

    public bool TryGet(string action, out IAccessPolicy? policy) =>
        policies.TryGetValue(action, out policy);
}
