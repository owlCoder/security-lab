using Microsoft.Extensions.Options;
using Platform.Api.Modules.Identity.SessionExpiry;

namespace Platform.Identity.Tests.Helpers;

public static class SessionOptionsTestDefaults
{
    public static IOptions<SessionOptions> Create() =>
        Options.Create(new SessionOptions
        {
            AbsoluteLifetimeMinutes = 480,
            ActivityTimeoutMinutes = 45,
            MfaPendingTimeoutMinutes = 5
        });
}
