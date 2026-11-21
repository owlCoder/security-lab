
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories;

public static class CatalogModule
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAccessAction<CatalogReadPolicy>(
            new ActionDefinition(CatalogReadPolicy.ActionId, ActionScope.Authenticated, null, false));

        if (!string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("Authoring")))
        {
            services.AddScoped<CatalogService>();
        }

        return services;
    }

    internal static bool RequiresSession(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action
            == CatalogReadPolicy.ActionId;

    public static IEndpointRouteBuilder MapCatalog(
        this IEndpointRouteBuilder app,
        IConfiguration configuration)
    {
        // Test hosts can apply configuration after service registration. Only map
        // routes when AddCatalog actually registered their required service.
        if (app.ServiceProvider.GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(CatalogService)))
        {
            app.MapCatalogEndpoints();
        }

        return app;
    }
}
