
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories;

public static class CatalogModule
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("Authoring")))
        {
            services.AddScoped<CatalogService>();
        }

        return services;
    }

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
