using System.Net;
using Microsoft.Extensions.Options;

namespace Platform.Api.Modules.Identity.RateLimiting;

/// <summary>
/// Pragovi i poverljivi proxy-ji. Čitaju se iz konfiguracije/okruženja (ADM-04), npr.
/// <c>RateLimiting__Login__PermitLimit=10</c> ili <c>RateLimiting__KnownProxies__0=10.0.0.5</c>.
/// </summary>
public sealed class IdentityRateLimitOptions
{
    public const string Section = "RateLimiting";

    public WindowLimit Login { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public WindowLimit PasswordReset { get; set; } = new() { PermitLimit = 10, WindowSeconds = 900 };

    /// <summary>Tačne IP adrese reverse proxy-ja čiji se X-Forwarded-For prihvata.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Mreže reverse proxy-ja u CIDR obliku, npr. "10.0.0.0/8".</summary>
    public string[] KnownNetworks { get; set; } = [];
}

public sealed class WindowLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

/// <summary>Neispravna konfiguracija zaustavlja pokretanje umesto da limiter tiho ne radi.</summary>
public sealed class IdentityRateLimitOptionsValidator : IValidateOptions<IdentityRateLimitOptions>
{
    public const int MaxPermitLimit = 10_000;
    public const int MaxWindowSeconds = 86_400;

    public ValidateOptionsResult Validate(string? name, IdentityRateLimitOptions options)
    {
        var errors = new List<string>();
        CheckLimit(errors, nameof(options.Login), options.Login);
        CheckLimit(errors, nameof(options.PasswordReset), options.PasswordReset);

        foreach (var proxy in options.KnownProxies)
            if (!IPAddress.TryParse(proxy, out _))
                errors.Add($"RateLimiting:KnownProxies sadrzi neispravnu IP adresu '{proxy}'.");

        foreach (var network in options.KnownNetworks)
            if (!System.Net.IPNetwork.TryParse(network, out _))
                errors.Add($"RateLimiting:KnownNetworks sadrzi neispravnu mrezu '{network}' (ocekuje se CIDR, npr. 10.0.0.0/8).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void CheckLimit(List<string> errors, string name, WindowLimit? limit)
    {
        if (limit is null) { errors.Add($"RateLimiting:{name} nije podesen."); return; }
        if (limit.PermitLimit is < 1 or > MaxPermitLimit)
            errors.Add($"RateLimiting:{name}:PermitLimit mora biti izmedju 1 i {MaxPermitLimit}.");
        if (limit.WindowSeconds is < 1 or > MaxWindowSeconds)
            errors.Add($"RateLimiting:{name}:WindowSeconds mora biti izmedju 1 i {MaxWindowSeconds}.");
    }
}
