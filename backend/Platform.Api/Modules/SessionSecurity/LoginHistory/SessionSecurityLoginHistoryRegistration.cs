using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;

// IDN-22 TIM 09
namespace Platform.Api.Modules.SessionSecurity.LoginHistory;

/// <summary>Temporary until AuditLoginHistorySource (IDN-22). The in-memory source starts empty:
/// there is no seed data in the real application.</summary>
public static class SessionSecurityLoginHistoryRegistration
{
    public static IServiceCollection AddSessionSecurityLoginHistory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ILoginHistorySource>(
            new InMemoryLoginHistorySource(Array.Empty<LoginEvent>()));
        return services;
    }
}
