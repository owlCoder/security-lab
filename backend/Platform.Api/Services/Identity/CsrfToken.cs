using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-15 / SEC-03: names and cookie settings of the CSRF token, shared by the token endpoint
/// (<c>GET /api/auth/csrf</c>), the antiforgery registration and <c>CsrfProtectionMiddleware</c>.
/// </summary>
public static class CsrfToken
{
    /// <summary>Request header in which the frontend sends the request token.</summary>
    public const string HeaderName = "X-CSRF-TOKEN";

    /// <summary>
    /// HttpOnly cookie with the antiforgery cookie token, the secret half of the pair. The <c>__Host-</c> prefix makes
    /// the browser accept it only with <c>Secure</c>, <c>Path=/</c> and no <c>Domain</c>, so a sibling subdomain
    /// cannot plant its own value.
    /// </summary>
    public const string CookieName = "__Host-SecurityLab.Csrf";

    /// <summary>
    /// Readable cookie with the request token. Our own JavaScript copies it into <see cref="HeaderName"/>;
    /// a foreign page cannot read it, so it cannot build the header.
    /// </summary>
    public const string ReadableCookieName = "__Host-SecurityLab.CsrfToken";

    /// <summary>Stable error code of a rejected request, so the client knows to fetch a new token.</summary>
    public const string InvalidCode = "csrf_invalid";

    /// <summary>Same flags as the session cookie. No <c>Expires</c>: it lives as long as the browser session.</summary>
    public static CookieOptions Options() => Build(httpOnly: true);

    /// <summary>As <see cref="Options"/>, but readable by the frontend's JavaScript.</summary>
    public static CookieOptions ReadableOptions() => Build(httpOnly: false);

    /// <summary>
    /// Issues a request token for the current session (see <see cref="CsrfSessionBinding"/>) and writes both cookies.
    /// A new cookie token is only issued when the browser has no valid one yet, so other tabs keep working.
    /// </summary>
    public static string Issue(HttpContext context, IAntiforgery antiforgery)
    {
        // The check runs before authentication, so it always sees an anonymous user. Issue the token for an
        // anonymous user too: with the KT1 demo on (its cookie scheme is then the only, default scheme) the
        // endpoint would otherwise bind the token to the KT1 user and every check would refuse it.
        // The binding that matters is to the session cookie (CsrfSessionBinding).
        var user = context.User;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        AntiforgeryTokenSet tokens;
        try
        {
            tokens = antiforgery.GetTokens(context);
        }
        finally
        {
            context.User = user;
        }
        if (tokens.CookieToken is not null)
            context.Response.Cookies.Append(CookieName, tokens.CookieToken, Options());
        context.Response.Cookies.Append(ReadableCookieName, tokens.RequestToken!, ReadableOptions());
        return tokens.RequestToken!;
    }

    /// <summary>Removes both cookies, so the next state-changing request needs a new pair.</summary>
    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, Options());
        response.Cookies.Delete(ReadableCookieName, ReadableOptions());
    }

    private static CookieOptions Build(bool httpOnly) => new()
    {
        HttpOnly = httpOnly,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    };
}
