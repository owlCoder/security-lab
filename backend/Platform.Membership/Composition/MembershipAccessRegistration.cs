using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Membership.Application;

namespace Platform.Membership.Composition;

public static class MembershipAccessRegistration
{
    public static IServiceProvider RegisterMembershipAccess(
        this IServiceProvider services)
    {
        var catalog = services
            .GetRequiredService<InMemoryActionCatalog>();

        var registry = services
            .GetRequiredService<InMemoryAccessPolicyRegistry>();

        //catalog.Register(MembershipActions.EnrollMember);
        catalog.Register(new ActionDefinition(MembershipActions.EnrollMember, ActionScope.Course, MembershipActions.CourseResourceType, true));

        registry.Register(
            MembershipActions.EnrollMember,
            ActivatorUtilities.CreateInstance<EnrollCourseMemberPolicy>(
                services));

        return services;
    }
}
