using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Membership.Application;

namespace Platform.Membership.Tests.Testing;

/// <summary>
/// ACC-07: pravi odlučivač Access modula (podrazumevano odbijanje) sa jednom radnjom
/// delegacije i zadatom politikom. Za testove poslovnih pravila koristi AllowAllPolicy.
/// </summary>
internal static class DelegationTestAccess
{
    public static IAccessDecision AllowAll(string action) => With(action, new AllowAllPolicy());

    public static IAccessDecision With(string action, IAccessPolicy policy)
    {
        var catalog = new InMemoryActionCatalog();
        var registry = new InMemoryAccessPolicyRegistry();
        catalog.Register(new ActionDefinition(
            action,
            ActionScope.Course,
            DelegationActions.CourseResourceType,
            action != DelegationActions.Read));
        registry.Register(action, policy);
        return new DefaultDenyAccessDecision(catalog, registry, new NoAuditWriter(), new AccessDecisionOptions());
    }
}
