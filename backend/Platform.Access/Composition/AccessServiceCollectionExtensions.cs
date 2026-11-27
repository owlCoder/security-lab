using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Application.Services;
using Platform.Access.Application.Options;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;
using Platform.Access.Application.Rules;
using Platform.Access.Application.Snapshots;
using Platform.Access.Infrastructure.Guards;
using Platform.Access.Infrastructure.Grants;

namespace Platform.Access.Composition;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddAccessAction<TPolicy>(
        this IServiceCollection services,
        ActionDefinition definition)
        where TPolicy : class, IAccessPolicy
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureRegistrationOpen(services);
        ArgumentNullException.ThrowIfNull(definition);
        ActionCatalogValidator.Validate(definition);

        if (AccessActions.All.Any(action => action.Id == definition.Id) ||
            services.GetAccessPolicyRegistrations().Any(registration =>
                registration.Definition?.Id == definition.Id))
        {
            throw new InvalidOperationException(
                $"Action '{definition.Id}' is already registered.");
        }

        RegisterPolicy<TPolicy>(services, definition.Id);
        services.AddSingleton(new AccessPolicyRegistration(definition.Id, definition, typeof(TPolicy)));
        return services;
    }

    public static IServiceCollection AddAccessPolicy<TPolicy>(
        this IServiceCollection services,
        string action)
        where TPolicy : class, IAccessPolicy
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureRegistrationOpen(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        if (!ActionId.IsValid(action))
        {
            throw new AccessConfigurationException(
                $"Policy cannot be registered: invalid ActionId (length {action.Length}).");
        }

        var knownAction = AccessActions.All.Any(definition => definition.Id == action) ||
                          services.GetAccessPolicyRegistrations().Any(registration =>
                              registration.Definition?.Id == action);
        if (!knownAction)
        {
            throw new AccessConfigurationException(
                $"Policy cannot be registered for unknown action '{action}'.");
        }

        if (CourseRolePolicies.All.Any(definition => definition.Action.Id == action) ||
            services.GetAccessPolicyRegistrations().Any(registration =>
                registration.Action == action))
        {
            throw new InvalidOperationException(
                $"Policy for '{action}' is already registered.");
        }

        RegisterPolicy<TPolicy>(services, action);
        services.AddSingleton(new AccessPolicyRegistration(action, null, typeof(TPolicy)));
        return services;
    }

    public static IServiceCollection AddAccessControl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(AccessControlServicesRegistered)))
        {
            return services;
        }

        services.AddSingleton<AccessControlServicesRegistered>();
        services.TryAddSingleton(new AccessDecisionOptions());
        services.TryAddSingleton(new AccessConfigurationOptions
        {
            RequirePolicyCoverage = true
        });
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<InMemoryActionCatalog>(sp =>
            new InMemoryActionCatalog(
                sp.GetServices<AccessPolicyRegistration>()
                    .Where(registration => registration.Definition is not null)
                    .Select(registration => registration.Definition!),
                freezeAfterConstruction: true));
        services.AddSingleton<IActionCatalog>(sp => sp.GetRequiredService<InMemoryActionCatalog>());
        services.TryAddSingleton<IMembershipReader, EmptyMembershipReader>();
        services.TryAddSingleton<IResourceDescriptorResolver, EmptyResourceDescriptorResolver>();
        services.AddSingleton<InMemoryAccessPolicyRegistry>(sp =>
        {
            var registry = new InMemoryAccessPolicyRegistry();
            var membershipReader = sp.GetRequiredService<IMembershipReader>();
            var resourceDescriptorResolver = sp.GetRequiredService<IResourceDescriptorResolver>();

                    
            var contextualRuleSets = sp
                .GetServices<ContextualRuleSetRegistration>()
                .ToDictionary(
                    registration => registration.Action,
                    registration => registration.Rules,
                    StringComparer.Ordinal);

            var snapshotLoader =
                sp.GetRequiredService<IEvaluationSnapshotLoader>();

            var ownershipGuard =
                sp.GetRequiredService<OwnershipScopeGuard>();

            foreach (var definition in CourseRolePolicies.All)
            {
                IAccessPolicy policy = new CourseRoleAccessPolicy(
                    definition,
                    membershipReader,
                    resourceDescriptorResolver);


                contextualRuleSets.TryGetValue(
                    definition.Action.Id,
                    out var ruleSet);

                policy = new ContextualAccessPolicy(
                    definition.Action.Id,
                    policy,
                    snapshotLoader,
                    ownershipGuard,
                    ruleSet);

                registry.Register(definition.Action.Id, policy);
            }

            foreach (var registration in sp.GetServices<AccessPolicyRegistration>())
            {
                var policy = sp.GetRequiredService(registration.PolicyType) as IAccessPolicy
                    ?? throw new AccessConfigurationException(
                        $"Registered policy for '{registration.Action}' does not implement IAccessPolicy.");
                registry.Register(
                    registration.Action,
                    policy);
            }

            registry.Freeze();
            return registry;
        });
        services.AddSingleton<IAccessPolicyRegistry>(sp =>
            sp.GetRequiredService<InMemoryAccessPolicyRegistry>());
        services.TryAddSingleton<IAccessAuditWriter>(sp =>
            new LoggerAccessAuditWriter(
                sp.GetService<ILogger<LoggerAccessAuditWriter>>()
                    ?? NullLogger<LoggerAccessAuditWriter>.Instance));
        services.AddSingleton<IAccessDecision, DefaultDenyAccessDecision>();
        services.AddSingleton<AccessConfigurationStartupCheck>(services =>
            new AccessConfigurationStartupCheck(
                services.GetRequiredService<IActionCatalog>(),
                services.GetRequiredService<InMemoryAccessPolicyRegistry>(),
                services.GetRequiredService<InMemoryActionCatalog>(),
                services.GetRequiredService<AccessConfigurationOptions>(),
                services.GetService<ILogger<AccessConfigurationStartupCheck>>()
                    ?? NullLogger<AccessConfigurationStartupCheck>.Instance));
        services.AddSingleton<IHostedService>(services =>
            services.GetRequiredService<AccessConfigurationStartupCheck>());
        services.TryAddSingleton<IEvaluationSnapshotLoader, EvaluationSnapshotLoader>();
        services.TryAddSingleton<IElevatedGrantReader, EmptyElevatedGrantReader>();
        services.TryAddSingleton<OwnershipScopeGuard>();

        return services;
    }

    private static void RegisterPolicy<TPolicy>(IServiceCollection services, string action)
        where TPolicy : class, IAccessPolicy
    {
        if (typeof(TPolicy).IsAbstract)
        {
            throw new InvalidOperationException(
                $"Policy '{typeof(TPolicy).Name}' for '{action}' must be a concrete class.");
        }

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(TPolicy))
            .ToArray();

        if (registrations.Length > 1 ||
            registrations.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Singleton))
        {
            throw new InvalidOperationException(
                $"Policy '{typeof(TPolicy).Name}' for '{action}' must have exactly one singleton registration.");
        }

        if (registrations.Length == 0)
        {
            services.AddSingleton<TPolicy>();
        }
    }

    private static IEnumerable<AccessPolicyRegistration> GetAccessPolicyRegistrations(
        this IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(AccessPolicyRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<AccessPolicyRegistration>();

    private static void EnsureRegistrationOpen(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AccessControlServicesRegistered)))
        {
            throw new InvalidOperationException(
                "Register all Access actions and policies before AddAccessControl(), then call AddAccessControl() once before building the host.");
        }
    }

    internal sealed record AccessPolicyRegistration(
        string Action,
        ActionDefinition? Definition,
        Type PolicyType);

    private sealed class AccessControlServicesRegistered
    {
    }
    public static IServiceCollection AddContextualRules(
    this IServiceCollection services,
    string action,
    RuleSet<EvaluationSnapshot> rules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(rules);

        EnsureRegistrationOpen(services);

        var knownAction = CourseRolePolicies.All.Any(
            definition => definition.Action.Id == action);

        if (!knownAction)
        {
            throw new AccessConfigurationException(
                $"Contextual rules cannot be registered for unknown core action '{action}'.");
        }

        ContextualRuleIds.Validate(rules);

        var alreadyRegistered = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(ContextualRuleSetRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ContextualRuleSetRegistration>()
            .Any(registration => registration.Action == action);

        if (alreadyRegistered)
        {
            throw new InvalidOperationException(
                $"Contextual rules for '{action}' are already registered.");
        }

        services.AddSingleton(
            new ContextualRuleSetRegistration(action, rules));

        return services;
    }
    internal sealed record ContextualRuleSetRegistration(
        string Action,
        RuleSet<EvaluationSnapshot> Rules);

    public static IServiceCollection AddDefaultContextualRules(
    this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var entry in ContextualAccessRules.Create())
        {
            services.AddContextualRules(
                entry.Key,
                entry.Value);
        }

        return services;
    }
}
