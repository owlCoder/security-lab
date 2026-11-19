using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Competencies;

public static class CompetenciesModule
{
    // Call once before builder.Build(). Existing ACC/LAB/Identity policies stay unchanged.
    public static IServiceCollection AddPrg04CompetencyOperations(this IServiceCollection services)
    {
        services.AddScoped<Prg04SessionIdentity>();
        services.AddScoped<CompetencyLinkService>();
        services.AddAccessAction<CompetencyLinkManagePolicy>(new ActionDefinition(
            CompetencyLinkActions.Manage, ActionScope.Course, "laboratory", IsPrivileged: true));
        return services;
    }
}

