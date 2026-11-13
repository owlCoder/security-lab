using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// IDN-07 through the real HTTP pipeline: routing, SessionCheckMiddleware, the real SessionManager and PostgreSQL.
/// Proves that (1) a request without a valid session is 401 and a valid session without permission is 403,
/// and (2) a revoked session is refused on the very next request, with the other sessions untouched.
/// The 403 route is the IDN-03 admin endpoint, which is part of the same module.
/// </summary>
[Category("IDN-07")]
public class SessionReviewRevokeIntegrationTests : IntegrationTestBase
{
    private const string Password = "Password123!";
    private const string SessionsUrl = "/api/auth/sessions";
    private const string MeUrl = "/api/auth/me";

    // Cookies are sent explicitly per request, so no cookie jar can hide a stale or cleared cookie.
    private static HttpClient _http = null!;

    [OneTimeSetUp]
    public async Task Prepare()
    {
        // A denied admin action is audited in the same database, so the audit schema must exist.
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);
        _http = NewClient(Factory);
    }

    [OneTimeTearDown]
    public void Release() => _http.Dispose();

    // 401 versus 403

    [Test]
    public async Task ProtectedRoute_WithoutCookie_Returns401SessionInvalid_AndHasNoCookieToClear()
    {
        var response = await SendAsync(_http, HttpMethod.Put, StatusUrl(Guid.NewGuid()), body: new { status = "BLOCKED" });

        await AssertSessionInvalidAsync(response, cookieCleared: false);
    }

    [Test]
    public async Task ProtectedRoute_WithUnknownToken_Returns401SessionInvalid_AndClearsTheCookie()
    {
        var response = await SendAsync(_http, HttpMethod.Put, StatusUrl(Guid.NewGuid()), "not-a-real-token",
            new { status = "BLOCKED" });

        await AssertSessionInvalidAsync(response, cookieCleared: true);
    }

    [Test]
    public async Task ProtectedRoute_ValidSessionWithoutPermission_Returns403_AndChangesNothing()
    {
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, student.Id);

        var response = await SendAsync(_http, HttpMethod.Put, StatusUrl(other.Id), session.RawToken,
            new { status = "BLOCKED" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await CodeAsync(response), Is.EqualTo("forbidden"));
        Assert.That((await UserTestData.GetAsync(Factory, other.Id))!.Status,
            Is.EqualTo(Platform.Identity.Enums.AccountStatus.Active));
        Assert.That((await SendAsync(_http, HttpMethod.Get, MeUrl, session.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "a 403 must not end the caller's session");
    }

    [TestCase("Development")]
    [TestCase("Production")]
    public async Task RevokedSession_Gets401OnTheNextRequest_WhilePermissionDenialStays403(string environment)
    {
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, student.Id);
        var target = await SessionTestData.CreateAsync(Factory, student.Id);

        using var host = environment == "Development"
            ? null
            : Factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var ownClient = host is null ? null : NewClient(host);
        var client = ownClient ?? _http;

        var denyUrl = StatusUrl(other.Id);
        var body = new { status = "BLOCKED" };

        // Before the revocation the target session is valid: authenticated, but not permitted -> 403.
        Assert.That((await SendAsync(client, HttpMethod.Put, denyUrl, target.RawToken, body)).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await SendAsync(client, HttpMethod.Get, MeUrl, target.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var revoke = await SendAsync(client, HttpMethod.Delete, $"{SessionsUrl}/{target.Session.SessionHandle}", caller.RawToken);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Next request with the same cookie: 401, not 403, on the permission-checked route and on a plain one.
        await AssertSessionInvalidAsync(
            await SendAsync(client, HttpMethod.Put, denyUrl, target.RawToken, body), cookieCleared: true);
        await AssertSessionInvalidAsync(
            await SendAsync(client, HttpMethod.Get, MeUrl, target.RawToken), cookieCleared: true);

        // The caller's own session is unaffected and is still 403 (valid, no permission), not 401.
        Assert.That((await SendAsync(client, HttpMethod.Put, denyUrl, caller.RawToken, body)).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await UserTestData.GetAsync(Factory, other.Id))!.Status,
            Is.EqualTo(Platform.Identity.Enums.AccountStatus.Active));
    }

    // Revoking sessions

    [Test]
    public async Task RevokeOtherSession_RejectsItImmediately_AndKeepsTheCallersSessionValid()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        Assert.That((await SendAsync(_http, HttpMethod.Get, MeUrl, otherDevice.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var response = await SendAsync(_http, HttpMethod.Delete,
            $"{SessionsUrl}/{otherDevice.Session.SessionHandle}", caller.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        await AssertSessionInvalidAsync(
            await SendAsync(_http, HttpMethod.Get, MeUrl, otherDevice.RawToken), cookieCleared: true);
        await AssertSessionInvalidAsync(
            await SendAsync(_http, HttpMethod.Get, SessionsUrl, otherDevice.RawToken), cookieCleared: true);

        var remaining = await SendAsync(_http, HttpMethod.Get, SessionsUrl, caller.RawToken);
        Assert.That(remaining.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var handles = (await remaining.Content.ReadFromJsonAsync<List<SessionInfo>>())!.Select(s => s.Handle);
        Assert.That(handles, Is.EquivalentTo(new[] { caller.Session.SessionHandle }));
    }

    [Test]
    public async Task RevokeCurrentSession_RejectsTheSameCookieOnTheNextRequest()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var current = await SessionTestData.CreateAsync(Factory, user.Id);

        var revoke = await SendAsync(_http, HttpMethod.Delete,
            $"{SessionsUrl}/{current.Session.SessionHandle}", current.RawToken);

        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await AssertSessionInvalidAsync(
            await SendAsync(_http, HttpMethod.Get, SessionsUrl, current.RawToken), cookieCleared: true);
        await AssertSessionInvalidAsync(
            await SendAsync(_http, HttpMethod.Delete, $"{SessionsUrl}/{current.Session.SessionHandle}", current.RawToken),
            cookieCleared: true);
        Assert.That((await SessionTestData.GetAsync(Factory, current.Session.Id))!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task RevokeSessionOfAnotherUser_Returns404_IndistinguishableFromUnknown_AndRevokesNothing()
    {
        var attacker = await UserTestData.AddAsync(Factory, "attacker@example.com", Password);
        var victim = await UserTestData.AddAsync(Factory, "victim@example.com", Password);
        var attackerSession = await SessionTestData.CreateAsync(Factory, attacker.Id);
        var victimSession = await SessionTestData.CreateAsync(Factory, victim.Id);

        var foreign = await SendAsync(_http, HttpMethod.Delete,
            $"{SessionsUrl}/{victimSession.Session.SessionHandle}", attackerSession.RawToken);
        var unknown = await SendAsync(_http, HttpMethod.Delete,
            $"{SessionsUrl}/{Guid.NewGuid()}", attackerSession.RawToken);

        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await unknown.Content.ReadAsStringAsync()),
            "a foreign handle must not be distinguishable from an unknown one");
        Assert.That((await SessionTestData.GetAsync(Factory, victimSession.Session.Id))!.RevokedAt, Is.Null);
        Assert.That((await SendAsync(_http, HttpMethod.Get, MeUrl, victimSession.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
    }

    // Reviewing sessions

    [Test]
    public async Task ListSessions_ReturnsOnlyTheCallersActiveSessions_WithoutCredentialMaterial()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var stranger = await UserTestData.AddAsync(Factory, "stranger@example.com", Password);
        var first = await SessionTestData.CreateAsync(Factory, user.Id);
        var second = await SessionTestData.CreateAsync(Factory, user.Id);
        var revoked = await SessionTestData.CreateAsync(Factory, user.Id);
        var expired = await SessionTestData.CreateAsync(Factory, user.Id, lifetime: TimeSpan.FromMinutes(-1));
        var foreign = await SessionTestData.CreateAsync(Factory, stranger.Id);
        Assert.That(await GetService<ISessionManager>().RevokeSession(user.Id, revoked.Session.SessionHandle), Is.True);

        var response = await SendAsync(_http, HttpMethod.Get, SessionsUrl, first.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var json = await response.Content.ReadAsStringAsync();
        var handles = JsonSerializer.Deserialize<List<SessionInfo>>(json, JsonSerializerOptions.Web)!.Select(s => s.Handle);
        Assert.That(handles, Is.EquivalentTo(new[] { first.Session.SessionHandle, second.Session.SessionHandle }));
        foreach (var issued in new[] { first, second, revoked, expired, foreign })
            Assert.That(json, Does.Not.Contain(issued.RawToken));
        using var document = JsonDocument.Parse(json);
        foreach (var item in document.RootElement.EnumerateArray())
            Assert.That(item.EnumerateObject().Select(p => p.Name.ToLowerInvariant()),
                Is.EquivalentTo(new[] { "handle", "createdat", "lastactivityat", "expiresat" }));
    }

    // Helpers

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string StatusUrl(Guid accountId) => $"/api/admin/accounts/{accountId}/status";

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task AssertSessionInvalidAsync(HttpResponseMessage response, bool cookieCleared)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await CodeAsync(response), Is.EqualTo(SessionCheckMiddleware.InvalidSessionCode));

        var cleared = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal)
                                     && cookie.Contains("1970", StringComparison.Ordinal));
        Assert.That(cleared, Is.EqualTo(cookieCleared));
    }
}
