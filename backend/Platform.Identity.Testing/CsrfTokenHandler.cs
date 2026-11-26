using System.Collections.Concurrent;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Testing;

/// <summary>
/// US-15: makes a test client behave like our frontend. Before a POST, PUT, PATCH or DELETE it fetches a CSRF pair
/// from <c>GET /api/auth/csrf</c>, sending the same session cookie as the request, because the token is bound to the
/// session. It keeps one pair per session and adds the cookie and the <c>X-CSRF-TOKEN</c> header to every
/// state-changing request. A request that already has the header is left alone, so a test can still send a wrong one.
/// Tests that simulate a CSRF attack must use a client without this handler.
/// </summary>
public sealed class CsrfTokenHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, CsrfPair> _pairs = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsSafe(request.Method) && !request.Headers.Contains(CsrfToken.HeaderName))
        {
            var pair = await GetPairAsync(request.RequestUri!, SessionOf(request), cancellationToken);
            pair.AppendCookie(request.Headers);
            request.Headers.Add(CsrfToken.HeaderName, pair.Token);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsSafe(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options || method == HttpMethod.Trace;

    // The session token in the request's Cookie header, or "" without one.
    private static string SessionOf(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("Cookie", out var values))
            return string.Empty;

        var prefix = SessionCookie.Name + "=";
        return values
            .SelectMany(value => value.Split(';'))
            .Select(part => part.Trim())
            .Where(part => part.StartsWith(prefix, StringComparison.Ordinal))
            .Select(part => part[prefix.Length..])
            .LastOrDefault() ?? string.Empty;
    }

    private async Task<CsrfPair> GetPairAsync(Uri requestUri, string session, CancellationToken cancellationToken)
    {
        if (_pairs.TryGetValue(session, out var cached))
            return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pairs.TryGetValue(session, out cached))
                return cached;

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(requestUri, CsrfPair.TokenUrl));
            if (session.Length > 0)
                tokenRequest.Headers.Add("Cookie", $"{SessionCookie.Name}={session}");
            using var response = await base.SendAsync(tokenRequest, cancellationToken);
            var pair = await CsrfPair.ReadAsync(response, cancellationToken);
            _pairs[session] = pair;
            return pair;
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _gate.Dispose();
        base.Dispose(disposing);
    }
}
