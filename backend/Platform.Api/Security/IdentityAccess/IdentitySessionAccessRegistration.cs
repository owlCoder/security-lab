using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Security.IdentityAccess;

public static class IdentitySessionAccessRegistration
{
    /// <summary>
    /// IDN-03 and IDN-07 on the central Access module: policies and actions, and the session-based subject
    /// provider. Call before <c>AddAccessControl()</c>; <c>ISessionManager</c> and <c>IAdminDirectory</c>
    /// are registered by Program.cs.
    /// </summary>
    public static IServiceCollection AddIdentitySessionAccess(this IServiceCollection services)
    {
        // IDN-03: central action, so only its policy is added (it is no longer in ActionsWithoutPolicy).
        services.AddAccessPolicy<AccountStatusChangePolicy>(IdentityAccessActions.AccountStatusChange);

        // IDN-07: session review and revocation. Authenticated scope and no resource type: the owner is the
        // caller, not a course.
        services
            .AddAccessAction<SessionSelfServicePolicy>(
                new ActionDefinition(IdentityAccessActions.SessionList, ActionScope.Authenticated, null, false))
            .AddAccessAction<SessionSelfServicePolicy>(
                new ActionDefinition(IdentityAccessActions.SessionRevoke, ActionScope.Authenticated, null, false))
            .AddAccessAction<SessionNotificationPolicy>(
                new ActionDefinition(IdentityAccessActions.SessionNotificationsRead, ActionScope.Authenticated, null, false))
            .AddAccessAction<SessionNotificationPolicy>(
                new ActionDefinition(IdentityAccessActions.SessionNotificationsUpdate, ActionScope.Authenticated, null, false));

        // Decorator around the currently registered subject provider (anonymous if there is none yet).
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IAccessSubjectProvider));
        if (original is not null)
            services.Remove(original);

        services.AddScoped<IAccessSubjectProvider>(sp =>
            new IdentitySessionSubjectProvider(
                CreateInner(sp, original),
                sp.GetRequiredService<ISessionManager>()));

        return services;
    }

    private static IAccessSubjectProvider CreateInner(IServiceProvider sp, ServiceDescriptor? original) =>
        original switch
        {
            null => new AnonymousAccessSubjectProvider(),
            { ImplementationInstance: IAccessSubjectProvider instance } => instance,
            { ImplementationFactory: { } factory } => (IAccessSubjectProvider)factory(sp),
            _ => (IAccessSubjectProvider)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!),
        };
}
