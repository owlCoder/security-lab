using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-15 / SEC-03: binds the CSRF request token to the session cookie the browser sends with it. The token carries
/// the SHA-256 of <c>SecurityLab.Session</c> (empty without a session), so a pair issued before login, after logout
/// or for another session in the same browser is refused. The value is inside the encrypted token and never leaves it.
/// <c>HttpContext.User</c> cannot do this: the session is not an authentication scheme and the check runs before
/// authentication, so it always sees an anonymous user; <c>CsrfToken.Issue</c> therefore issues for an anonymous user.
/// </summary>
public sealed class CsrfSessionBinding : IAntiforgeryAdditionalDataProvider
{
    /// <summary>
    /// Login sets the new session token here before it issues a new request token, because the new session cookie
    /// is only in the response, not yet in the request.
    /// </summary>
    public const string SessionOverrideKey = "US-15.CsrfSessionToken";

    public string GetAdditionalData(HttpContext context) => Bind(SessionOf(context));

    public bool ValidateAdditionalData(HttpContext context, string additionalData) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Bind(SessionOf(context))),
            Encoding.ASCII.GetBytes(additionalData ?? string.Empty));

    private static string? SessionOf(HttpContext context) =>
        context.Items.TryGetValue(SessionOverrideKey, out var value) && value is string token
            ? token
            : context.Request.Cookies[SessionCookie.Name];

    private static string Bind(string? sessionToken) =>
        string.IsNullOrEmpty(sessionToken)
            ? string.Empty
            : WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
}
