using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>
/// IDN-11: registers the session store of the rotation use case. <see cref="IdentitySessionRotationStore"/>
/// keeps rotated sessions in identity.sessions (Team 1), so the Team 1 session check rejects the old token
/// and accepts the new one. Scoped, because it uses the request's PlatformIdentityDbContext.
/// </summary>
public static class SessionSecuritySessionsRegistration
{
    public static IServiceCollection AddSessionSecuritySessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISessionRotationStore, IdentitySessionRotationStore>();
        return services;
    }
}
