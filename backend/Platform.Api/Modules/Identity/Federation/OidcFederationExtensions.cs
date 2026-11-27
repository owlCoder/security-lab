using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Modules.Identity.Federation;

/// <summary>
/// IDN-08: OpenID Connect login (authorization code + PKCE). Token validation, state, nonce and
/// PKCE are done by Microsoft.AspNetCore.Authentication.OpenIdConnect, not by our code.
/// </summary>
public static class OidcFederationExtensions
{
    public const string Scheme = "Idn08Oidc";
    public const string ExternalCookieScheme = "Idn08External";
    public const string CallbackPath = "/api/auth/oidc/callback";

    public static bool AddOidcFederation(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(OidcFederationOptions.Section);
        services.Configure<OidcFederationOptions>(section);

        var settings = section.Get<OidcFederationOptions>() ?? new OidcFederationOptions();
        if (!settings.Enabled)
            return false;

        settings.Validate(environment.IsDevelopment());

        services.AddAuthentication()
            // Required by the remote handler as its sign-in scheme. The login is always completed in
            // OnTicketReceived (HandleResponse), so nothing is ever signed in with this cookie.
            .AddCookie(ExternalCookieScheme, cookie =>
            {
                cookie.Cookie.Name = "SecurityLab.External";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                cookie.SlidingExpiration = false;
            })
            .AddOpenIdConnect(Scheme, oidc =>
            {
                oidc.SignInScheme = ExternalCookieScheme;
                oidc.Authority = settings.Authority;
                oidc.ClientId = settings.ClientId;          // public client: no secret in config or Git
                oidc.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.CallbackPath = CallbackPath;

                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("email");

                oidc.MapInboundClaims = false;              // keep "sub", "email", "email_verified" as named
                oidc.GetClaimsFromUserInfoEndpoint = false; // the validated id_token is the only source
                oidc.SaveTokens = false;                    // provider tokens are never stored
                oidc.Events = new OpenIdConnectEvents
                {
                    // Fixed callback address from configuration; the same value is used later for the code exchange.
                    OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = settings.Url(CallbackPath);
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = CompleteLoginAsync,
                    OnRemoteFailure = HandleRemoteFailureAsync,
                    OnAccessDenied = HandleAccessDeniedAsync
                };
            });

        return true;
    }

    private static async Task CompleteLoginAsync(TicketReceivedContext context)
    {
        context.HandleResponse();

        var http = context.HttpContext;
        var settings = http.RequestServices.GetRequiredService<IOptions<OidcFederationOptions>>().Value;
        var login = http.RequestServices.GetRequiredService<IFederatedLoginService>();

        var result = await login.ExecuteAsync(context.Principal!, http.TraceIdentifier, http.RequestAborted);

        http.Response.Headers.CacheControl = "no-store";
        if (result.Outcome == LoginOutcome.Success)
        {
            // Same cookie as password login (IDN-02); the token never appears in a URL or body.
            http.Response.Cookies.Append(
                SessionCookie.Name, result.Session!.Token, SessionCookie.Options(result.Session.ExpiresAt));
            http.Response.Redirect(GetRedirectUrl(http, settings.SuccessRedirect));
            return;
        }

        http.Response.Redirect(GetRedirectUrl(http, settings.FailureRedirect));
    }


    private static async Task HandleAccessDeniedAsync(AccessDeniedContext context)
    {
        context.HandleResponse();

        var http = context.HttpContext;
        var settings = http.RequestServices
            .GetRequiredService<IOptions<OidcFederationOptions>>()
            .Value;

        var audit = http.RequestServices
            .GetRequiredService<IdentityAuditProducer>();

        var logger = http.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Platform.Api.Identity.Oidc");

        logger.LogWarning(
            "OIDC authorization was denied by the identity provider");

        await audit.LoginFailedAsync(
            "oidc_access_denied",
            http.TraceIdentifier,
            null,
            CancellationToken.None);

        http.Response.Headers.CacheControl = "no-store";

        http.Response.Redirect(
            GetRedirectUrl(http, settings.FailureRedirect));
    }


    private static async Task HandleRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();

        var http = context.HttpContext;
        var settings = http.RequestServices.GetRequiredService<IOptions<OidcFederationOptions>>().Value;
        var audit = http.RequestServices.GetRequiredService<IdentityAuditProducer>();
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Platform.Api.Identity.Oidc");

        // Bad state, nonce, signature, a cancelled login... Only the exception type is logged, never tokens.
        logger.LogWarning("OIDC login failed: {FailureType}", context.Failure?.GetType().Name);
        await audit.LoginFailedAsync("oidc_remote_failure", http.TraceIdentifier, null, CancellationToken.None);

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Redirect(GetRedirectUrl(http, settings.FailureRedirect));
    }
        // Absolute address on the frontend origin from configuration; path is a validated local path.
    private static string GetRedirectUrl(HttpContext http, string path) =>
        http.RequestServices.GetRequiredService<IOptions<OidcFederationOptions>>().Value.Url(path);
}