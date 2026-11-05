using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Platform.Api.Modules.Laboratories;

public static class LaboratoriesModule
{
   // Registracija servisa modula
    public static IServiceCollection AddLaboratories(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();

        // LAB-01 API: skladiste i slucajevi upotrebe.
        // KT1: skladiste u memoriji (singleton, podaci nestaju posle restarta).
        // KT2: ovde se registruje PostgreSQL implementacija istog ILaboratoryRepository.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILaboratoryRepository, InMemoryLaboratoryRepository>();
        services.AddScoped<LaboratoryService>();

        if (environment.IsDevelopment())
        {
            // FAKE implementacije - samo u Development rezimu.
            // FakeIdentityContext - IDN modul (tim 18)
            // FakeAccessDecision  - ACC modul (tim 3)
            services.AddScoped<IIdentityContext, FakeIdentityContext>();
            services.AddSingleton<IAccessDecision, FakeAccessDecision>();
        }
        else
        {
            // Niko nije prepoznat, sve je odbijeno.
            services.AddScoped<IIdentityContext, AnonymousIdentityContext>();
            services.AddSingleton<IAccessDecision, DenyAllAccessDecision>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapLaboratories(this IEndpointRouteBuilder app)
    {
        app.MapLaboratoriesEndpoints();
        return app;
    }
}