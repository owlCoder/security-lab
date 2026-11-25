using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Access.Contracts;
using Platform.Access.Infrastructure.Development;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;

namespace Platform.Access.Composition;

public static class AccessDevelopmentServiceCollectionExtensions
{
    
    public const string DevelopmentSourcesKey = "Access:DevelopmentSources";

    
    public static IServiceCollection AddAccessDevelopmentSources(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!environment.IsDevelopment() || !IsEnabled(configuration))
        {
            return services;
        }

        if (HasResourceSourceRegistration(services))
        {
            throw new InvalidOperationException(
                $"Development resource sources cannot be combined with registered resource sources. " +
                $"Disable {DevelopmentSourcesKey} and register the real resource sources instead.");
        }

        var memberships = new InMemoryMembershipReader();
        var resources = new InMemoryResourceDescriptorResolver();
        AccessDevelopmentSeed.Populate(memberships, resources);

        services.TryAddSingleton<IMembershipReader>(memberships);

        
        services.TryAddSingleton<IResourceDescriptorResolver>(resources);
        services.TryAddSingleton<IResourceOwnershipResolver>(resources);

        foreach (var resolver in AccessDevelopmentSeed.CreateAttributeResolvers())
        {
            services.AddSingleton<IResourceAttributeResolver>(resolver);
        }

        return services;
    }

    private static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(configuration[DevelopmentSourcesKey], "true", StringComparison.OrdinalIgnoreCase);

    private static bool HasResourceSourceRegistration(IServiceCollection services) =>
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IResourceDescriptorResolver) ||
            descriptor.ServiceType == typeof(IResourceOwnershipResolver) ||
            descriptor.ServiceType == typeof(IResourceAttributeResolver));
}
