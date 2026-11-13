using Microsoft.Extensions.DependencyInjection;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-04 / ASN-06 TIM 15
public static class AssignmentsAccessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the authorization adapters for the Assignments module. Requires AddAccessControl()
    /// and the Identity module (IIdentityContext) to be registered. The adapters are scoped because
    /// the identity context may use a scoped database context.
    /// </summary>
    public static IServiceCollection AddAssignmentsAccess(this IServiceCollection services)
    {
        services.AddScoped<IAccountStateReader, IdentityAccountStateReader>();
        services.AddScoped<IDeadlineExtensionAuthorization, AccessDeadlineExtensionAuthorization>();
        services.AddScoped<IStartAttemptAuthorization, AccessStartAttemptAuthorization>();
        services.AddScoped<ICourseEnrollmentCheck, MembershipCourseEnrollmentCheck>();
        return services;
    }
}
