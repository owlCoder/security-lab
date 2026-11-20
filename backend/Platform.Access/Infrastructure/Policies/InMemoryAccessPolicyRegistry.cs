using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Policies;

public sealed class InMemoryAccessPolicyRegistry : IAccessPolicyRegistry
{
    private readonly Dictionary<string, IAccessPolicy> policies =
        new(StringComparer.Ordinal);
    private readonly object registrationLock = new();
    private bool isFrozen;

    public IReadOnlyCollection<string> RegisteredActions
    {
        get
        {
            lock (registrationLock)
            {
                return policies.Keys.ToArray();
            }
        }
    }

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

        lock (registrationLock)
        {
            if (isFrozen)
            {
                throw new InvalidOperationException(
                    "Access policies cannot be registered after application startup.");
            }

            if (!policies.TryAdd(action, policy))
            {
                throw new InvalidOperationException($"Policy for '{action}' is already registered.");
            }
        }
    }

    internal void Freeze()
    {
        lock (registrationLock)
        {
            isFrozen = true;
        }
    }

    public bool TryGet(string action, out IAccessPolicy? policy)
    {
        lock (registrationLock)
        {
            return policies.TryGetValue(action, out policy);
        }
    }
}
