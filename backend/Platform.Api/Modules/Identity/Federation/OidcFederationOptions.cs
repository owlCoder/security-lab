namespace Platform.Api.Modules.Identity.Federation;

/// <summary>IDN-08 settings, section "Oidc". Disabled by default, so other teams need no identity provider.</summary>
public sealed class OidcFederationOptions
{
    public const string Section = "Oidc";

    public bool Enabled { get; set; }
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public bool RequireHttpsMetadata { get; set; } = true;

    public string? PublicOrigin { get; set; }

    internal string Url(string path) => PublicOrigin!.TrimEnd('/') + path;

    /// <summary>Local paths only: the user can never choose where the login redirects (no open redirect).</summary>
    public string SuccessRedirect { get; set; } = "/";
    public string FailureRedirect { get; set; } = "/login?oidc=failed";

    /// <summary>An invalid setting stops the API from starting instead of failing at the first login.</summary>
    internal void Validate(bool isDevelopment)
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority))
            throw new InvalidOperationException("Oidc:Authority must be an absolute URL.");
        if (RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Oidc:Authority must use HTTPS.");
        if (!RequireHttpsMetadata && !isDevelopment)
            throw new InvalidOperationException("Oidc:RequireHttpsMetadata can be false only in Development.");
                if (!Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) ||
            origin.AbsolutePath != "/" || origin.Query.Length > 0)
            throw new InvalidOperationException("Oidc:PublicOrigin must be an origin such as https://lab.example.org.");
        if (origin.Scheme != Uri.UriSchemeHttps && !isDevelopment)
            throw new InvalidOperationException("Oidc:PublicOrigin must use HTTPS.");
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("Oidc:ClientId is required.");
        if (!IsLocalPath(SuccessRedirect) || !IsLocalPath(FailureRedirect))
            throw new InvalidOperationException("Oidc redirects must be local paths.");
    }

    private static bool IsLocalPath(string? path) =>
        !string.IsNullOrEmpty(path) && path[0] == '/' &&
        (path.Length == 1 || (path[1] != '/' && path[1] != '\\'));
}