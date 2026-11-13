using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
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

        AddLaboratoryAccess(services);

        return services;
    }

    // #185: radnje i politika za centralni ACC-05 middleware. Mora pre AddAccessControl()
    // (na grani ACC-05 se AddLaboratories poziva pre njega; main tu registraciju jos ne proverava).
    private static void AddLaboratoryAccess(IServiceCollection services)
    {
        foreach (string action in LabActions.All)
        {
            services.AddAccessAction<LaboratoryAccessPolicy>(
                new ActionDefinition(action, ActionScope.Authenticated, null, false));
        }

        // Dekorator oko trenutno registrovanog provajdera subjekta (ili anonimnog, ako ga jos nema).
        ServiceDescriptor? original = services.LastOrDefault(d => d.ServiceType == typeof(IAccessSubjectProvider));
        if (original is not null)
        {
            services.Remove(original);
        }

        services.AddScoped<IAccessSubjectProvider>(sp =>
            new LaboratorySubjectProvider(CreateInnerSubjectProvider(sp, original)));
    }

    private static IAccessSubjectProvider CreateInnerSubjectProvider(IServiceProvider sp, ServiceDescriptor? original) =>
        original switch
        {
            null => new AnonymousAccessSubjectProvider(),
            { ImplementationInstance: IAccessSubjectProvider instance } => instance,
            { ImplementationFactory: { } factory } => (IAccessSubjectProvider)factory(sp),
            _ => (IAccessSubjectProvider)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!),
        };

    public static IEndpointRouteBuilder MapLaboratories(this IEndpointRouteBuilder app)
    {
        app.MapLaboratoriesEndpoints();
        return app;
    }
}