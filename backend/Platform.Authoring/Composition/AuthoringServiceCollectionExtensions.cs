using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Authoring.Application;
using Platform.Authoring.Infrastructure;
using Platform.Access.Composition;

namespace Platform.Authoring.Composition;

/// <summary>The single entry point Platform.Api uses for the Authoring module.</summary>
public static class AuthoringServiceCollectionExtensions
{
    /// <summary>
    /// Everything here is a singleton because every dependency is
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
    /// Registers Authoring actions and policies before AddAccessControl(). 
    /// </summary>
    
    public static IServiceCollection AddAuthoringAccess(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach(var action in AuthoringActions.All)
        {
            var isCopy = action == AuthoringActions.TemplateCopy;
            services.AddAccessAction<AuthoringAuthorPolicy>(new ActionDefinition(action, isCopy ? ActionScope.Course : ActionScope.Authenticated, isCopy ? AuthoringActions.TemplateResourceType : null, false));
        }

        return services;
    }
}
