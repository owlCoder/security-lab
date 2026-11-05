using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Application.Services;
using Platform.Access.Application.Options;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;

namespace Platform.Access.Composition;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddAccessControl(this IServiceCollection services)
    {
        services.TryAddSingleton(new AccessDecisionOptions());
        services.TryAddSingleton(new AccessConfigurationOptions());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AccessAuditOptions>();

        services.AddSingleton<InMemoryActionCatalog>();
        services.AddSingleton<IActionCatalog>(sp => sp.GetRequiredService<InMemoryActionCatalog>());
        services.TryAddSingleton<IMembershipReader, EmptyMembershipReader>();
        services.TryAddSingleton<IResourceDescriptorResolver, EmptyResourceDescriptorResolver>();
        services.AddSingleton<InMemoryAccessPolicyRegistry>(sp =>
        {
            var registry = new InMemoryAccessPolicyRegistry();
            var membershipReader = sp.GetRequiredService<IMembershipReader>();
            var resourceDescriptorResolver = sp.GetRequiredService<IResourceDescriptorResolver>();

            foreach (var definition in CourseRolePolicies.All)
            {
                registry.Register(
                    definition.Action.Id,
                    new CourseRoleAccessPolicy(
                        definition,
                        membershipReader,
                        resourceDescriptorResolver));
            }

            return registry;
        });
        services.AddSingleton<IAccessPolicyRegistry>(sp => sp.GetRequiredService<InMemoryAccessPolicyRegistry>());
        services.AddSingleton(CreateAggregatingSink);
        services.AddSingleton<IAccessAuditWriter>(CreateAuditWriter);
        services.AddSingleton<AccessAuditFlushService>(services =>
            new AccessAuditFlushService(
                services.GetRequiredService<AggregatingAccessAuditSink>(),
                services.GetRequiredService<AccessAuditOptions>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetService<ILogger<AccessAuditFlushService>>()
                    ?? NullLogger<AccessAuditFlushService>.Instance));
        services.AddSingleton<IHostedService>(services => services.GetRequiredService<AccessAuditFlushService>());
        services.AddSingleton<IAccessDecision, DefaultDenyAccessDecision>();
        services.AddSingleton<AccessConfigurationStartupCheck>(services =>
            new AccessConfigurationStartupCheck(
                services.GetRequiredService<IActionCatalog>(),
                services.GetRequiredService<InMemoryAccessPolicyRegistry>(),
                services.GetRequiredService<AccessConfigurationOptions>(),
                services.GetService<ILogger<AccessConfigurationStartupCheck>>()
                    ?? NullLogger<AccessConfigurationStartupCheck>.Instance));
        services.AddSingleton<IHostedService>(services =>
            services.GetRequiredService<AccessConfigurationStartupCheck>());

        return services;
    }

    private static AggregatingAccessAuditSink CreateAggregatingSink(IServiceProvider services)
    {
        var logger = new LoggerAccessAuditSink(
            services.GetService<ILogger<LoggerAccessAuditSink>>()
                ?? NullLogger<LoggerAccessAuditSink>.Instance);
        return new AggregatingAccessAuditSink(
            logger, services.GetRequiredService<AccessAuditOptions>(), services.GetRequiredService<TimeProvider>());
    }

    private static ResilientAccessAuditWriter CreateAuditWriter(IServiceProvider services)
    {
        var options = services.GetRequiredService<AccessAuditOptions>();
        var clock = services.GetRequiredService<TimeProvider>();

        var filtering = new FilteringAccessAuditWriter(
            new AccessAuditRecordFactory(clock, options),
            services.GetRequiredService<AggregatingAccessAuditSink>(),
            options);

        return new ResilientAccessAuditWriter(
            filtering,
            options,
            clock,
            services.GetService<ILogger<ResilientAccessAuditWriter>>()
                ?? NullLogger<ResilientAccessAuditWriter>.Instance);
    }
}
