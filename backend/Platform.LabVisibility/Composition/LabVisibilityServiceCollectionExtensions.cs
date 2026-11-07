using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Application.Preview;
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
        services.AddSingleton<InMemoryLabPublicContentReader>();
        services.AddSingleton<ILabPublicContentReader>(sp => sp.GetRequiredService<InMemoryLabPublicContentReader>());

        services.AddSingleton<InMemoryEvaluatorKeyStore>();
        services.AddSingleton<IEvaluatorKeyStore>(sp => sp.GetRequiredService<InMemoryEvaluatorKeyStore>());

        services.AddSingleton<InMemoryCourseMembership>();
        services.AddSingleton<ICourseMembership>(sp => sp.GetRequiredService<InMemoryCourseMembership>());

        services.AddLogging(); // bezbedno i ako je logging već registrovan; audit piše u log
        services.AddSingleton<ILabVisibilityAudit, LoggerLabVisibilityAudit>();
        services.AddSingleton<EvaluatorKeyReader>();

        // AUT-07: pregled pre objave (Cvetojević)
        services.AddScoped<PreviewStudentView>();

        return services;
    }

    // Poziva se jednom, posle builder.Build(). Radnje bez politike tim 3 odbija (NoPolicy).
    public static IServiceProvider RegisterLabVisibilityAccess(this IServiceProvider services)
    {
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        foreach (var action in LabVisibilityActions.All)
        {
            // lab.key.read i lab.preview (AUT-07) nose resurs "lab" (labId iz rute);
            // ostale radnje za sada nemaju resurs u LabVisibility ugovorima.
            var isKeyRead = action == LabVisibilityActions.KeyRead;
            var hasLabResource = isKeyRead || action == LabVisibilityActions.Preview;
            catalog.Register(new ActionDefinition(
                action,
                hasLabResource ? ActionScope.Course : ActionScope.Authenticated,
                hasLabResource ? "lab" : null,
                isKeyRead));
        }

        // Politike se dodaju ovde, svako svoju, istim obrascem.
        var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();
        policies.Register(
            LabVisibilityActions.KeyRead,
            ActivatorUtilities.CreateInstance<EvaluatorKeyPolicy>(services));

        // AUT-07: pregled pre objave samo za nastavnika kursa (Cvetojević)
        policies.Register(
            LabVisibilityActions.Preview,
            ActivatorUtilities.CreateInstance<LabPreviewPolicy>(services));

        return services;
    }
}
