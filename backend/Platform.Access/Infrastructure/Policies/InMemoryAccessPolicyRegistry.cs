using System.Collections.Concurrent;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Policies;

public sealed class InMemoryAccessPolicyRegistry : IAccessPolicyRegistry
{
    private readonly ConcurrentDictionary<string, IAccessPolicy> policies =
        new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> RegisteredActions => policies.Keys.ToArray();

    public void Register(string action, IAccessPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(policy);

        if (!ActionId.IsValid(action))
        {
            throw new AccessConfigurationException(
                $"Policy cannot be registered: invalid ActionId (length {action.Length}).");
        }

        if (string.IsNullOrWhiteSpace(policy.Id) || string.IsNullOrWhiteSpace(policy.Version))
        {
            throw new AccessConfigurationException(
                $"Policy for '{action}' must declare a non-empty Id and Version.");
        }

        if (!policies.TryAdd(action, policy))
        {
            throw new InvalidOperationException($"Policy for '{action}' is already registered.");
        }
    }

    public bool TryGet(string action, out IAccessPolicy? policy)
    {
        if (action is null)
        {
            policy = null;
            return false;
        }

        return policies.TryGetValue(action, out policy);
    }
}
