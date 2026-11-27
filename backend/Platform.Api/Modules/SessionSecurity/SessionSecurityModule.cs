using Platform.Api.Modules.SessionSecurity.Audit;
using Platform.Api.Modules.SessionSecurity.LoginHistory;
using Platform.Api.Modules.SessionSecurity.Rotation;
using Platform.Api.Modules.SessionSecurity.Sessions;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity;

/// <summary>IDN-11 / IDN-22: composition of Platform.SessionSecurity in the API host.</summary>
public static class SessionSecurityModule
{
    public static IServiceCollection AddSessionSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSessionSecuritySessions();
        services.AddSessionSecurityAudit();
        services.AddSessionSecurityLoginHistory();
        services.AddSessionSecurityRotation();
        return services;
    }
}
