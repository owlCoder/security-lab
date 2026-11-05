using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Policies;

namespace Platform.LabVisibility.Composition;

public static class LabVisibilityServiceCollectionExtensions
{
    // Poziva se posle AddAccessControl(). Svako dodaje svoju liniju ovde.
    public static IServiceCollection AddLabVisibility(this IServiceCollection services)
    {
        // AUT-04 (Antonić): PostgreSQL kad postoji ConnectionStrings:LabVisibility, inače InMemory.
        // Reader i store su singletoni (koriste ih singleton politike), pa EF ide preko fabrike DbContext-a.
        services.AddSingleton<InMemoryLabPublicContentReader>();
        services.AddSingleton<InMemoryEvaluatorKeyStore>();

        services.AddDbContextFactory<LabVisibilityDbContext>((sp, options) =>
            options.UseNpgsql(
                LabVisibilityDatabase.GetConnectionString(sp),
                LabVisibilityDesignTimeDbContextFactory.Configure));
        services.AddSingleton<EfLabPublicContentReader>();
        services.AddSingleton<EfEvaluatorKeyStore>();

        services.AddSingleton<ILabPublicContentReader>(sp => LabVisibilityDatabase.IsConfigured(sp)
            ? sp.GetRequiredService<EfLabPublicContentReader>()
            : sp.GetRequiredService<InMemoryLabPublicContentReader>());
        services.AddSingleton<IEvaluatorKeyStore>(sp => LabVisibilityDatabase.IsConfigured(sp)
            ? sp.GetRequiredService<EfEvaluatorKeyStore>()
            : sp.GetRequiredService<InMemoryEvaluatorKeyStore>());

        services.AddSingleton<InMemoryCourseMembership>();
        services.AddSingleton<ICourseMembership>(sp => sp.GetRequiredService<InMemoryCourseMembership>());

        services.AddLogging(); // bezbedno i ako je logging već registrovan; audit piše u log
        services.AddSingleton<ILabVisibilityAudit, LoggerLabVisibilityAudit>();
        services.AddSingleton<EvaluatorKeyReader>();

        return services;
    }

    // Poziva se jednom, posle builder.Build(). Radnje bez politike tim 3 odbija (NoPolicy).
    public static IServiceProvider RegisterLabVisibilityAccess(this IServiceProvider services)
    {
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        foreach (var action in LabVisibilityActions.All)
        {
            // The evaluator key request contains a lab resource; other actions
            // do not carry a resource in the current LabVisibility contracts.
            var isKeyRead = action == LabVisibilityActions.KeyRead;
            catalog.Register(new ActionDefinition(
                action,
                isKeyRead ? ActionScope.Course : ActionScope.Authenticated,
                isKeyRead ? "lab" : null,
                isKeyRead));
        }

        // Politike se dodaju ovde, svako svoju, istim obrascem.
        var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();
        policies.Register(
            LabVisibilityActions.KeyRead,
            ActivatorUtilities.CreateInstance<EvaluatorKeyPolicy>(services));

        return services;
    }
}
