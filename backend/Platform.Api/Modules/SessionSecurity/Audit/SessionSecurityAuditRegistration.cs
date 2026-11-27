using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Audit;

/// <summary>Temporary until AuditSessionSecurityAuditWriter (IDN-11).</summary>
public static class SessionSecurityAuditRegistration
{
    public static IServiceCollection AddSessionSecurityAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISessionSecurityAuditWriter, UnavailableSessionSecurityAuditWriter>();
        return services;
    }
}
