using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionAuthorizationRegistration
{
    public static IServiceCollection AddRevisionAuthorization(this IServiceCollection services)
    {
        services.TryAddSingleton<IRevisionAuthorReader, RevisionAuthorReader>();
        services.TryAddSingleton<IRevisionResourceReader, RevisionResourceReader>();
        services.TryAddSingleton<RevisionPermissions>();
        services.TryAddSingleton<IRevisionApprovalPermission>(sp => sp.GetRequiredService<RevisionPermissions>());
        services.TryAddSingleton<IRevisionActionPermission>(sp => sp.GetRequiredService<RevisionPermissions>());
        services.AddSingleton<RevisionApprovalPolicy>();
        services.AddSingleton<RevisionActionPolicy>();
        return services;
    }

    /// <summary>
    /// Declare revision actions before AddAccessControl freezes the catalog and registry.
    /// Shared by Program and focused integration tests; never mutate a built provider.
    /// </summary>
    public static IServiceCollection AddRevisionAccessPolicies(this IServiceCollection services)
    {
        foreach (var action in new[] { RevisionActions.Create, RevisionActions.Read,
                     RevisionActions.Mine, RevisionActions.PendingReviews, RevisionActions.Submit, RevisionActions.Return })
        {
            services.AddAccessAction<RevisionActionPolicy>(new ActionDefinition(
                action, ActionScope.Course, action == RevisionActions.Create ? "course" : "revision", false));
        }

        return services
            .AddAccessAction<RevisionApprovalPolicy>(new ActionDefinition(
                RevisionActions.Approve, ActionScope.Course, "revision", true))
            .AddAccessAction<RevisionActionPolicy>(new ActionDefinition(
                RevisionActions.UnlockAccount, ActionScope.System, null, true));
    }
}
