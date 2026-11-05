using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Authoring.Application;
using Platform.Authoring.Infrastructure;

namespace Platform.Authoring.Composition;

/// <summary>The single entry point Platform.Api uses for the Authoring module.</summary>
public static class AuthoringServiceCollectionExtensions
{
    /// <summary>
    /// Call after AddAccessControl(). Everything here is a singleton because every dependency is
    /// stateless or in-memory. When the PostgreSQL repository arrives, the services that use it
    /// become scoped (see docs/components/audit/DI-GUIDELINES.md).
    /// </summary>
    public static IServiceCollection AddAuthoring(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITemplateRepository, BuiltInTemplateRepository>();
        services.AddSingleton<IDraftRepository, InMemoryDraftStore>();
        services.AddSingleton<IAuthorDirectory, ConfigAuthorDirectory>();
        services.AddSingleton<IAuthoringAccess, DirectoryAuthoringAccess>();
        services.AddSingleton<DraftService>();
        services.AddSingleton<TemplateCatalogService>();

        return services;
    }

    /// <summary>
    /// Call once after builder.Build(). Registers the Authoring actions and their policy with the
    /// Access module. An action without a policy is denied by default.
    /// </summary>
    public static IServiceProvider RegisterAuthoringAccess(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();
        var policy = new AuthoringAuthorPolicy(services.GetRequiredService<IAuthorDirectory>());

        foreach (var action in AuthoringActions.All)
        {
            catalog.Register(action);
            policies.Register(action, policy);
        }

        return services;
    }
}
