using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Middleware;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration.Csrf;

/// <summary>
/// US-15 / SEC-03 through the real HTTP pipeline and PostgreSQL. A state-changing request that carries our session
/// cookie (or is a login) must also carry a valid CSRF token; without it the request is refused with
/// <c>403 csrf_invalid</c> and nothing changes, not even the session's last activity. The attack scenario is the
/// revocation of a session: a foreign page can make the browser send the cookie, but it cannot read the token or
/// add the header. The token is bound to the session cookie, so a pair only works with the session it was issued for.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("US-15")]
[Category("SEC-03")]
public class CsrfProtectionIntegrationTests : IntegrationTestBase
{
    private const string Password = "Password123!";
    private const string MeUrl = "/api/auth/me";
    private const string LoginUrl = "/api/auth/login";
    private const string LogoutUrl = "/api/auth/logout";
    private const string AttackerOrigin = "https://evil.example";
    private const string Kt1CookieName = "SecurityLab.CourseworkKt1";

    // Must be an id in Stub:AdminIds of appsettings.Development.json (as in AccountStatusChangeIntegrationTests).
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Cookies are sent explicitly per request, as a browser would: this client keeps no cookie jar and, unlike the
    // shared Client, does not add a CSRF token by itself.
    private static HttpClient _http = null!;

    [OneTimeSetUp]
    public void CreateBrowserClient() => _http = Factory.CreateDefaultClient();

    [OneTimeTearDown]
    public void DisposeBrowserClient() => _http.Dispose();

    // The unlock tests read audit.audit_events, as AccountUnlockIntegrationTests does.
    [OneTimeSetUp]
    public async Task MigrateAuditSchema() =>
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);

    // The token endpoint

    [Test]
    public async Task GetToken_WithoutCookies_ShouldReturn200_AndSetHostOnlyStrictCookies_AndNoStore()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, CsrfPair.TokenUrl);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);

        var setCookie = SetCookieHeader(response, CsrfToken.CookieName);
        Assert.That(setCookie, Does.Contain("httponly").IgnoreCase);
        Assert.That(setCookie, Does.Contain("secure").IgnoreCase);
        Assert.That(setCookie, Does.Contain("samesite=strict").IgnoreCase);
        Assert.That(setCookie, Does.Contain("path=/").IgnoreCase);
        Assert.That(setCookie, Does.Not.Contain("domain=").IgnoreCase, "a __Host- cookie must not name a domain");

        var token = await TokenAsync(response);
        Assert.That(token, Is.Not.Null.And.Not.Empty);
        Assert.That(setCookie, Does.Not.Contain(token!), "the header token and the cookie token are different values");

        // The request token is also in a cookie the frontend can read (not HttpOnly), with the same other flags.
        var readable = SetCookieHeader(response, CsrfToken.ReadableCookieName);
        Assert.That(readable, Does.StartWith($"{CsrfToken.ReadableCookieName}={token};"));
        Assert.That(readable, Does.Not.Contain("httponly").IgnoreCase);
        Assert.That(readable, Does.Contain("secure").IgnoreCase);
        Assert.That(readable, Does.Contain("samesite=strict").IgnoreCase);
        Assert.That(readable, Does.Contain("path=/").IgnoreCase);
    }

    [Test]
    public async Task GetToken_WithExistingCsrfCookie_ShouldKeepTheCookieToken()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, CsrfPair.TokenUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {pair.Cookie}");

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(SetsCookie(response, CsrfToken.CookieName), Is.False,
            "the HttpOnly cookie token is kept, so tokens already held by other tabs stay valid");
    }

    [Test]
    public async Task RevokeSession_RequestTokenRefreshedWithTheSameCookie_ShouldReturn204()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        using var again = new HttpRequestMessage(HttpMethod.Get, CsrfPair.TokenUrl);
        again.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {pair.Cookie}");
        using var refreshedResponse = await _http.SendAsync(again);
        var refreshed = pair with { Token = (await TokenAsync(refreshedResponse))! };
        using var request = RevokeRequest(otherDevice.Session);
        refreshed.ApplyTo(request, caller.RawToken);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // The attack (negative) and the normal case (positive)

    [Test]
    public async Task RevokeSession_SessionCookieWithoutToken_ShouldReturn403_AndKeepBothSessions()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "victim@example.com", Password);
        var victim = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        // What a foreign page can do: the browser adds the session cookie, the page cannot add a token.
        using var forged = RevokeRequest(otherDevice.Session);
        forged.Headers.Add("Cookie", $"{SessionCookie.Name}={victim.RawToken}");
        forged.Headers.Add("Origin", AttackerOrigin);

        // Act
        using var response = await _http.SendAsync(forged);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK), "the session was not revoked");
        Assert.That(await MeStatusAsync(victim.RawToken), Is.EqualTo(HttpStatusCode.OK), "a refused request does not end the session");
    }

    [Test]
    public async Task RevokeSession_SessionCookieAndValidToken_ShouldReturn204_AndRevokeTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        using var request = RevokeRequest(otherDevice.Session);
        pair.ApplyTo(request, caller.RawToken);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RevokeSession_TokenFromAnotherBrowser_ShouldReturn403_AndKeepTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var victimPair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        var attackerPair = await CsrfPair.FetchAsync(_http);
        // The attacker can get a valid token of their own, but it does not match the victim's cookie.
        using var request = RevokeRequest(otherDevice.Session);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {victimPair.Cookie}");
        request.Headers.Add(CsrfToken.HeaderName, attackerPair.Token);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("not-a-token")]
    [TestCase("")]
    public async Task RevokeSession_ForgedOrEmptyToken_ShouldReturn403_AndKeepTheSession(string token)
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        using var request = RevokeRequest(otherDevice.Session);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {pair.Cookie}");
        request.Headers.TryAddWithoutValidation(CsrfToken.HeaderName, token);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task RevokeSession_TokenWithoutItsCookie_ShouldReturn403_AndKeepTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);
        using var request = RevokeRequest(otherDevice.Session);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}");
        request.Headers.Add(CsrfToken.HeaderName, pair.Token);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    // Login CSRF

    [Test]
    public async Task Login_WithoutToken_ShouldReturn403_AndIssueNoSession()
    {
        // Arrange
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        using var request = LoginRequest("user@example.com");
        request.Headers.Add("Origin", AttackerOrigin);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(SetsCookie(response, SessionCookie.Name), Is.False);
    }

    [Test]
    public async Task Login_WithValidToken_ShouldReturn200_AndSetTheSessionCookie()
    {
        // Arrange
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var pair = await CsrfPair.FetchAsync(_http);
        using var request = LoginRequest("user@example.com");
        pair.ApplyTo(request);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(SetCookieValue(response, SessionCookie.Name), Is.Not.Null);
    }

    // What is deliberately not checked

    [Test]
    public async Task Logout_WithoutToken_ShouldReturn204_AndRevokeTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}");

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await MeStatusAsync(session.RawToken), Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetMe_SessionCookieWithoutToken_ShouldReturn200()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);

        // Act
        var status = await MeStatusAsync(session.RawToken);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK), "a safe request is not checked");
    }

    [Test]
    public async Task RevokeSession_WithoutSessionCookie_ShouldReturn401SessionInvalid_NotTheCsrfAnswer()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{Guid.NewGuid()}");

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await CodeAsync(response), Is.EqualTo(SessionCheckMiddleware.InvalidSessionCode));
    }

    // US-14: a refused request has no side effect

    [Test]
    public async Task RevokeSession_SessionCookieWithoutToken_ShouldReturn403_AndNotExtendTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var before = (await SessionTestData.GetAsync(Factory, caller.Session.Id))!.LastActivityAt;
        using var forged = RevokeRequest(otherDevice.Session);
        forged.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}");
        forged.Headers.Add("Origin", AttackerOrigin);

        // Act
        using var response = await _http.SendAsync(forged);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That((await SessionTestData.GetAsync(Factory, caller.Session.Id))!.LastActivityAt, Is.EqualTo(before),
            "a refused request must not count as activity (US-14 inactivity)");
    }

    // US-15: the token is bound to the session

    [Test]
    public async Task RevokeSession_TokenOfAnotherSession_ShouldReturn403_AndKeepTheSession()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherUser = await SessionTestData.CreateAsync(Factory, other.Id);
        var pairOfOtherUser = await CsrfPair.FetchAsync(_http, otherUser.RawToken);
        // A complete, valid pair, but issued for another session in the same browser.
        using var request = RevokeRequest(otherDevice.Session);
        pairOfOtherUser.ApplyTo(request, caller.RawToken);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Login_WithAnonymousToken_ShouldReturn200_AndIssueANewRequestToken()
    {
        // Arrange
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var anonymous = await CsrfPair.FetchAsync(_http);
        using var request = LoginRequest("user@example.com");
        anonymous.ApplyTo(request);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(SetCookieValue(response, SessionCookie.Name), Is.Not.Null);
        Assert.That(SetCookieValue(response, CsrfToken.ReadableCookieName), Is.Not.Null.And.Not.EqualTo(anonymous.Token),
            "login issues a request token for the new session");
    }

    [Test]
    public async Task RevokeSession_TokenIssuedBeforeLogin_ShouldReturn403()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var anonymous = await CsrfPair.FetchAsync(_http);
        var (session, _) = await LoginAsync(_http, "user@example.com", anonymous);
        using var request = RevokeRequest(otherDevice.Session);
        anonymous.ApplyTo(request, session);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
    }

    [Test]
    public async Task RevokeSession_TokenIssuedByLogin_ShouldReturn204()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var anonymous = await CsrfPair.FetchAsync(_http);
        var (session, issued) = await LoginAsync(_http, "user@example.com", anonymous);
        using var request = RevokeRequest(otherDevice.Session);
        (anonymous with { Token = issued }).ApplyTo(request, session);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logout_WithCsrfCookie_ShouldReturn204_AndRemoveBothCsrfCookies()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http, session.RawToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}; {pair.Cookie}");

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        foreach (var name in new[] { CsrfToken.CookieName, CsrfToken.ReadableCookieName })
            Assert.That(response.Headers.GetValues("Set-Cookie")
                    .Any(value => value.StartsWith(name + "=;", StringComparison.Ordinal) && value.Contains("1970")),
                Is.True, $"logout removes {name}");
    }

    [Test]
    public async Task Login_PairOfAnEndedSession_ShouldReturn403_AndIssueNoSession()
    {
        // Arrange
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, other.Id);
        var pairOfEndedSession = await CsrfPair.FetchAsync(_http, session.RawToken);
        using var logout = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        logout.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}; {pairOfEndedSession.Cookie}");
        using var loggedOut = await _http.SendAsync(logout);
        loggedOut.EnsureSuccessStatusCode();
        // The next user of the same browser logs in with the old pair.
        using var request = LoginRequest("user@example.com");
        pairOfEndedSession.ApplyTo(request);

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That(SetsCookie(response, SessionCookie.Name), Is.False);
    }

    // IDN-03 and IDN-07: the session check runs before the CSRF check

    [TestCase("revoked")]
    [TestCase("expired")]
    public async Task RevokeSession_InvalidSessionWithoutToken_ShouldReturn401SessionInvalid_AndRemoveTheCookie(string state)
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id,
            lifetime: state == "expired" ? TimeSpan.FromMinutes(-1) : null);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        if (state == "revoked")
            await GetService<ISessionStore>().RevokeByTokenAsync(session.RawToken, CancellationToken.None);
        using var request = RevokeRequest(otherDevice.Session);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}");

        // Act
        using var response = await _http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await CodeAsync(response), Is.EqualTo(SessionCheckMiddleware.InvalidSessionCode));
        Assert.That(response.Headers.GetValues("Set-Cookie")
                .Any(value => value.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal) && value.Contains("1970")),
            Is.True, "the rejected session cookie is removed");
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ChangeStatus_AdminWithoutToken_ShouldReturn403_AndChangeNoStatusOrSession()
    {
        // Arrange
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        // Act
        using var response = await StatusChangeWithoutTokenAsync(student.Id, adminSession.RawToken);

        // Assert
        await AssertCsrfRejectedAsync(response);
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Null);
        Assert.That(await MeStatusAsync(studentSession.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ChangeStatus_NonAdminWithoutToken_ShouldReturn403_AndTheSameBodyForExistingAndUnknownAccount()
    {
        // Arrange
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var target = await UserTestData.AddAsync(Factory, "target@example.com", Password);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        // Act
        // The check runs before Access and before any account is read, so the answer tells nothing about accounts.
        using var existing = await StatusChangeWithoutTokenAsync(target.Id, studentSession.RawToken);
        using var unknown = await StatusChangeWithoutTokenAsync(Guid.NewGuid(), studentSession.RawToken);

        // Assert
        await AssertCsrfRejectedAsync(existing);
        await AssertCsrfRejectedAsync(unknown);
        Assert.That(await existing.Content.ReadAsStringAsync(), Is.EqualTo(await unknown.Content.ReadAsStringAsync()));
        Assert.That((await UserTestData.GetAsync(Factory, target.Id))!.Status, Is.EqualTo(AccountStatus.Active));
    }

    // IDN-05: the KT1 demo cookie and the administrator unlock

    [Test]
    public async Task RevokeSession_Kt1CookieAndTokenIssuedByLogin_ShouldReturn204()
    {
        // Arrange
        // With KT1 on and OIDC off, the KT1 cookie scheme is the only (so the default) scheme: the token endpoint
        // and login run after UseAuthentication with the KT1 user, the check runs before it, as anonymous.
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        using var host = HostWithKt1Demo();
        using var http = host.CreateDefaultClient();
        var kt1Cookie = await Kt1SignInAsync(http);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, CsrfPair.TokenUrl);
        tokenRequest.Headers.Add("Cookie", kt1Cookie);
        using var tokenResponse = await http.SendAsync(tokenRequest);
        var pair = await CsrfPair.ReadAsync(tokenResponse);
        var (session, issued) = await LoginAsync(http, "user@example.com", pair, kt1Cookie);
        using var request = RevokeRequest(otherDevice.Session);
        request.Headers.Add("Cookie", kt1Cookie);
        (pair with { Token = issued }).ApplyTo(request, session);

        // Act
        using var response = await http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "the token that login issued with the KT1 user signed in works on the next request");
    }

    [Test]
    public async Task Unlock_AdminWithoutToken_ShouldReturn403_AndKeepTheLockout_AndRecordNoUnlock()
    {
        // Arrange
        var (adminSession, student) = await ArrangeLockedStudentAsync();
        var before = (await UserTestData.GetAsync(Factory, student.Id))!;
        using var host = HostWithRealAuditWriter();
        using var http = host.CreateDefaultClient();
        // No body: a page can send this as a simple form POST, without a CORS preflight.
        using var forged = UnlockRequest(student.Id);
        forged.Headers.Add("Cookie", $"{SessionCookie.Name}={adminSession}");
        forged.Headers.Add("Origin", AttackerOrigin);

        // Act
        using var response = await http.SendAsync(forged);

        // Assert
        await AssertCsrfRejectedAsync(response);
        var after = (await UserTestData.GetAsync(Factory, student.Id))!;
        Assert.That(after.LockoutEnd, Is.EqualTo(before.LockoutEnd));
        Assert.That(after.FailedLoginCount, Is.EqualTo(before.FailedLoginCount));
        Assert.That(await AuditTestData.RowsAsync(student.Id, "account.unlocked"), Is.Empty);
    }

    [Test]
    public async Task Unlock_AdminWithValidToken_ShouldReturn200_AndRecordTheUnlock()
    {
        // Arrange
        // The control for the test above: the same request with a valid token passes, so its 403 is the CSRF check.
        var (adminSession, student) = await ArrangeLockedStudentAsync();
        using var host = HostWithRealAuditWriter();
        using var http = host.CreateDefaultClient();
        var pair = await CsrfPair.FetchAsync(http, adminSession);
        using var request = UnlockRequest(student.Id);
        pair.ApplyTo(request, adminSession);

        // Act
        using var response = await http.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await AuditTestData.RowsAsync(student.Id, "account.unlocked"), Has.Count.EqualTo(1));
    }

    // Host variants

    private static WebApplicationFactory<Program> HostWithKt1Demo() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting("Kt1Demo:Enabled", "true"));

    // The real Audit writer, so "no account.unlocked row" means something (the shared factory stores nothing).
    private static WebApplicationFactory<Program> HostWithRealAuditWriter() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        }));

    // Helpers

    private static async Task<(string AdminSession, UserAccount Student)> ArrangeLockedStudentAsync()
    {
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        await UserTestData.LockAsync(Factory, student.Id, failedLogins: 2);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        return (adminSession.RawToken, student);
    }

    private static HttpRequestMessage RevokeRequest(Session session) =>
        new(HttpMethod.Delete, $"/api/auth/sessions/{session.SessionHandle}");

    private static HttpRequestMessage UnlockRequest(Guid accountId) =>
        new(HttpMethod.Post, $"/api/admin/accounts/{accountId}/unlock");

    private static HttpRequestMessage LoginRequest(string email) =>
        new(HttpMethod.Post, LoginUrl) { Content = JsonContent.Create(new { email, password = Password }) };

    // Logs in with the pair (and optional extra cookies); returns the new session token and the request token login issued.
    private static async Task<(string Session, string Token)> LoginAsync(
        HttpClient http, string email, CsrfPair pair, string? cookies = null)
    {
        using var request = LoginRequest(email);
        if (cookies is not null)
            request.Headers.Add("Cookie", cookies);
        pair.ApplyTo(request);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (SetCookieValue(response, SessionCookie.Name)!, SetCookieValue(response, CsrfToken.ReadableCookieName)!);
    }

    // Signs in the KT1 demo user "ana" and returns its cookie as name=value.
    private static async Task<string> Kt1SignInAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/kt1/session/ana");
        request.Headers.Add("X-Kt1-Demo", "1");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return $"{Kt1CookieName}={SetCookieValue(response, Kt1CookieName)}";
    }

    private static async Task<HttpResponseMessage> StatusChangeWithoutTokenAsync(Guid accountId, string sessionToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/accounts/{accountId}/status")
        {
            Content = JsonContent.Create(new { status = "BLOCKED" })
        };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");
        return await _http.SendAsync(request);
    }

    // The whole Set-Cookie header of one cookie.
    private static string SetCookieHeader(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));

    // Whether the response sets or removes this cookie at all.
    private static bool SetsCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(value => value.StartsWith(name + "=", StringComparison.Ordinal));

    // The value of a cookie the response sets, or null when it does not set it (or only removes it).
    private static string? SetCookieValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(value => value.StartsWith(name + "=", StringComparison.Ordinal))
                .Select(value => value.Split(';')[0][(name.Length + 1)..])
                .FirstOrDefault(value => value.Length > 0)
            : null;

    private static async Task<string?> TokenAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString();
    }

    private static async Task<HttpStatusCode> MeStatusAsync(string sessionToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");
        using var response = await _http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task AssertCsrfRejectedAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await CodeAsync(response), Is.EqualTo(CsrfToken.InvalidCode));
    }
}
