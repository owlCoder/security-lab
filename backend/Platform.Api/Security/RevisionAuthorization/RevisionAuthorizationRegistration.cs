using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionAuthorizationRegistration
{
    public static IServiceCollection AddRevisionAuthorization(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IRevisionAuthorReader, RevisionAuthorReader>();
        services.TryAddSingleton<IRevisionResourceReader, RevisionResourceReader>();
        services.TryAddSingleton<RevisionPermissions>();
        services.TryAddSingleton<IRevisionApprovalPermission>(
            sp => sp.GetRequiredService<RevisionPermissions>());
        services.TryAddSingleton<IRevisionActionPermission>(
            sp => sp.GetRequiredService<RevisionPermissions>());

        services.AddSingleton<RevisionApprovalPolicy>();
        services.AddSingleton<RevisionActionPolicy>();

        services.AddRevisionAuditForwarding();

        return services;
    }

    public static void RegisterRevisionPolicies(this IServiceProvider services)
    {
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        var registry = services.GetRequiredService<InMemoryAccessPolicyRegistry>();

        // Kreiranje revizije nad kursom
        catalog.Register(new ActionDefinition(
            RevisionActions.Create,
            ActionScope.Course,
            "course",
            false));

        // Citanje revizije
        catalog.Register(new ActionDefinition(
            RevisionActions.Read,
            ActionScope.Course,
            "revision",
            false));

        // Pregled vlastitih revizija
        catalog.Register(new ActionDefinition(
            RevisionActions.Mine,
            ActionScope.Course,
            "revision",
            false));

        // Pregled revizija koje cekaju odluku
        catalog.Register(new ActionDefinition(
            RevisionActions.PendingReviews,
            ActionScope.Course,
            "revision",
            false));

        // Slanje revizije
        catalog.Register(new ActionDefinition(
            RevisionActions.Submit,
            ActionScope.Course,
            "revision",
            false));

        registry.Register(
            RevisionActions.Submit,
            services.GetRequiredService<RevisionActionPolicy>());

        // Odobravanje revizije
        catalog.Register(new ActionDefinition(
            RevisionActions.Approve,
            ActionScope.Course,
            "revision",
            true));

        registry.Register(
            RevisionActions.Approve,
            services.GetRequiredService<RevisionApprovalPolicy>());

        // Vracanje revizije
        catalog.Register(new ActionDefinition(
            RevisionActions.Return,
            ActionScope.Course,
            "revision",
            false));

        registry.Register(
            RevisionActions.Return,
            services.GetRequiredService<RevisionActionPolicy>());

        // Otkljucavanje naloga
        catalog.Register(new ActionDefinition(
            RevisionActions.UnlockAccount,
            ActionScope.System,
            null,
            true));

        registry.Register(
            RevisionActions.UnlockAccount,
            services.GetRequiredService<RevisionActionPolicy>());

        foreach (var action in new[] { RevisionActions.Create, RevisionActions.Read,
                     RevisionActions.Mine, RevisionActions.PendingReviews })
            registry.Register(action, services.GetRequiredService<RevisionActionPolicy>());
    }
}
