using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Testing;

/// <summary>
/// US-15: a CSRF token pair as a browser holds it after <c>GET /api/auth/csrf</c>. The request token is bound to the
/// session cookie that was sent when it was issued, so a pair only works with that session (or without one).
/// </summary>
/// <param name="Cookie">The <c>name=value</c> part of the HttpOnly cookie, ready to be joined with other cookies.</param>
/// <param name="Token">The request token for the <c>X-CSRF-TOKEN</c> header.</param>
public sealed record CsrfPair(string Cookie, string Token)
{
    public const string TokenUrl = "/api/auth/csrf";

    /// <summary>Asks the API for a token pair, as the frontend does, for the given session (or none).</summary>
    public static async Task<CsrfPair> FetchAsync(
        HttpClient client, string? sessionToken = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TokenUrl);
        if (sessionToken is not null)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    /// <summary>
    /// Issues a pair inside the host, without an HTTP call, for tests whose host refuses every Access decision
    /// (so <c>GET /api/auth/csrf</c> is refused too) but still need a request to reach Access.
    /// </summary>
    public static CsrfPair Issue(IServiceProvider services, string? sessionToken = null)
    {
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (sessionToken is not null)
            context.Request.Headers.Cookie = $"{SessionCookie.Name}={sessionToken}";

        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetTokens(context);
        return new CsrfPair($"{CsrfToken.CookieName}={tokens.CookieToken}", tokens.RequestToken!);
    }

    /// <summary>Reads the pair from a <c>GET /api/auth/csrf</c> response.</summary>
    public static async Task<CsrfPair> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        response.EnsureSuccessStatusCode();

        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CsrfToken.CookieName + "=", StringComparison.Ordinal))
            .Split(';')[0];
        var body = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(cancellationToken);

        return new CsrfPair(cookie, body!.Token);
    }

    /// <summary>Adds the cookie (next to an optional session cookie) and the header to one request.</summary>
    public void ApplyTo(HttpRequestMessage request, string? sessionToken = null)
    {
        if (sessionToken is not null)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");

        ApplyTo(request.Headers);
    }

    /// <summary>The same for headers of a client (<c>DefaultRequestHeaders</c>) or of one request.</summary>
    public void ApplyTo(HttpRequestHeaders headers)
    {
        AppendCookie(headers);
        headers.Add(CsrfToken.HeaderName, Token);
    }

    /// <summary>The same for a request built directly on <c>TestServer.SendAsync</c>.</summary>
    public void ApplyTo(HttpRequest request)
    {
        var existing = request.Headers.Cookie.ToString();
        request.Headers.Cookie = string.IsNullOrEmpty(existing) ? Cookie : $"{existing}; {Cookie}";
        request.Headers[CsrfToken.HeaderName] = Token;
    }

    // One Cookie header, as a browser sends it: the CSRF cookie is joined to cookies the test already set.
    internal void AppendCookie(HttpRequestHeaders headers)
    {
        if (headers.TryGetValues("Cookie", out var existing))
        {
            var joined = string.Join("; ", existing.Append(Cookie));
            headers.Remove("Cookie");
            headers.Add("Cookie", joined);
            return;
        }

        headers.Add("Cookie", Cookie);
    }
}
