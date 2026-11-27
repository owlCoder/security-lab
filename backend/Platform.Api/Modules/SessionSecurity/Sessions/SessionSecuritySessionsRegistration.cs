using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>Temporary until IdentitySessionRotationStore (IDN-11).</summary>
public static class SessionSecuritySessionsRegistration
{
    public static IServiceCollection AddSessionSecuritySessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISessionRotationStore, InMemorySessionRotationStore>();
        return services;
    }
}
