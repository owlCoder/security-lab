using Platform.Access.Contracts;

namespace Platform.Api.Security.AccessAudit;

public static class AccessAuditRegistration
{
    public static IServiceCollection AddCentralAccessAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IAccessAuditWriter)))
        {
            throw new InvalidOperationException(
                "An access audit writer is already registered. Call AddCentralAccessAudit once, before AddAccessControl.");
        }

        services.AddSingleton<IAccessDeniedAuditMapper, AccessDeniedAuditMapper>();
        services.AddSingleton<IAccessAuditWriter, CentralAccessAuditWriter>();
        return services;
    }
}
