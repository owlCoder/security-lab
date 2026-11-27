using Microsoft.Extensions.Options;

namespace Platform.Api.Modules.Identity.SessionExpiry;

/// <summary>
/// US-14: Reads and validates session timeout configuration.
/// </summary>
public static class SessionOptionsExtensions
{
    public static IServiceCollection AddSessionExpiry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.Section))
            .Validate(
                options =>
                    options.AbsoluteLifetimeMinutes > 0 &&
                    options.ActivityTimeoutMinutes > 0 &&
                    options.MfaPendingTimeoutMinutes > 0,
                "All session timeout values must be positive.")
            .Validate(
                options => options.MfaPendingTimeoutMinutes < options.ActivityTimeoutMinutes,
                "MFA pending timeout must be less than the activity timeout.")
            .Validate(
                options => options.ActivityTimeoutMinutes <= options.AbsoluteLifetimeMinutes,
                "Activity timeout must not exceed the absolute session lifetime.")
            .ValidateOnStart();

        return services;
    }
}