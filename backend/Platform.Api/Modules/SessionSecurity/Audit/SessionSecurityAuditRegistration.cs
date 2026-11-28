using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Audit;

/// <summary>IDN-11 / AUD-01: session security audit records go to the Audit module (Team 12)
/// through AuditSessionSecurityAuditWriter.</summary>
public static class SessionSecurityAuditRegistration
{
    public static IServiceCollection AddSessionSecurityAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISessionSecurityAuditWriter, AuditSessionSecurityAuditWriter>();
        return services;
    }
}
