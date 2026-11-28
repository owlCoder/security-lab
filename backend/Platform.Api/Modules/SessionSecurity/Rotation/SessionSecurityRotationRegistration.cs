using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Rotation;

/// <summary>IDN-11: registers the rotation use case, the login history use case and the token
/// generator. The token generator and the use cases are implemented in Platform.SessionSecurity
/// and are not replaced after P0.</summary>
public static class SessionSecurityRotationRegistration
{
    public static IServiceCollection AddSessionSecurityRotation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISessionTokenGenerator, CryptoSessionTokenGenerator>();
        services.AddScoped<IRotateSessionUseCase, SessionRotationService>();
        services.AddScoped<ISearchLoginHistoryUseCase, LoginHistoryService>();
        return services;
    }
}
