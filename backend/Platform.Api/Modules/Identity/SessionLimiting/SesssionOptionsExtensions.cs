using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Platform.Api.Modules.Identity.SessionLimiting;



/// <summary>
/// IDN-06: Read from config, check validitiy
/// </summary>

public static class SessionOptionsExtensions
{
    public static IServiceCollection AddSessionLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.Section)).Validate(
                o =>
                    o.AbsoluteLifetimeMinutes > 0 &&
                    o.ActivityTimeoutMinutes > 0 &&
                    o.MfaPendingTimeoutMinutes > 0,
                ("All session timeout values must be positive.")
            )
            .ValidateOnStart();
       
        return services;
    }
}