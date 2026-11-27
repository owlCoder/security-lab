using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using OtpNet;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06 US-18: login with MFA enabled and the second factor (POST /api/auth/mfa/verify), including replay.</summary>
[NonParallelizable]
public sealed class MfaVerificationIntegrationTests : MfaIntegrationTestBase
{
    [Test]
    [Category("US-18")]
    public async Task Login_WithMfaEnabled_ReturnsMfaRequired()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-login-required@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());

        // Act
        var response = await LoginAsync("mfa-login-required@platform.com");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(body, Is.EqualTo(new LoginResponse(user.Id, true)));
    }

    [Test]
    [Category("US-18")]
    public async Task Login_WithoutMfa_DoesNotReturnMfaRequired()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-login-not-required@platform.com", Password);

        // Act
        var response = await LoginAsync("mfa-login-not-required@platform.com");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(body, Is.EqualTo(new LoginResponse(user.Id, false)));
    }

    // IDN-11: the second factor changes the privilege level, so the session identifier changes too.
    [Test]
    [Category("US-18")]
    [Category("IDN-11")]
    public async Task Verification_ValidCode_ReplacesPendingSessionWithNewActiveSession()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-verify@platform.com");
        var pending = await GetCurrentSessionAsync();
        var pendingToken = CurrentSessionToken();

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = MfaTestData.CurrentCode(secret) });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var activeToken = SessionTokenFrom(response);
        Assert.That(activeToken, Is.Not.EqualTo(pendingToken));
        var active = await SessionTestData.FindByTokenAsync(Factory, activeToken);
        Assert.That(active!.AuthLevel, Is.EqualTo(AuthLevel.Active));
        Assert.That(active.UserId, Is.EqualTo(user.Id));
        Assert.That(active.SessionHandle, Is.Not.EqualTo(pending!.SessionHandle));
        var replaced = await SessionTestData.GetAsync(Factory, pending.Id);
        Assert.That(replaced!.RevokedAt, Is.Not.Null, "the pending session is revoked");
        Assert.That(replaced.AuthLevel, Is.EqualTo(AuthLevel.MfaPending), "the pending session is never promoted");
        Assert.That((await GetWithSessionAsync("/api/auth/me", activeToken)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    [Category("US-18")]
    [Category("IDN-11")]
    public async Task Verification_ValidCode_TheCookieFromBeforeTheSecondFactorIsRejectedAfterwards()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-old-cookie@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-verify-old-cookie@platform.com");
        var pendingToken = CurrentSessionToken();
        var verify = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = MfaTestData.CurrentCode(secret) });
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        var me = await GetWithSessionAsync("/api/auth/me", pendingToken);
        var sessions = await GetWithSessionAsync("/api/auth/sessions", pendingToken);

        // Assert
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(sessions.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // US-15 and IDN-11: the CSRF token is bound to the session, and verify replaces the session, so verify issues
    // a token for the new session. The token of the pending session is refused afterwards.
    [Test]
    [Category("US-18")]
    [Category("US-15")]
    public async Task Verification_ValidCode_IssuesACsrfTokenForTheNewSession_AndTheOldTokenIsRejected()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-csrf@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var pending = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        using var browser = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var pendingPair = await CsrfPair.FetchAsync(browser, pending.RawToken);
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/verify")
        {
            Content = JsonContent.Create(new { Code = MfaTestData.CurrentCode(secret) })
        };
        pendingPair.ApplyTo(verifyRequest, pending.RawToken);

        // Act
        var verify = await browser.SendAsync(verifyRequest);
        var activeToken = SessionTokenFrom(verify);
        var activePair = pendingPair with { Token = ReadableCsrfTokenFrom(verify) };
        var withNewToken = await EnrollWithAsync(browser, activeToken, activePair);
        var withOldToken = await EnrollWithAsync(browser, activeToken, pendingPair);

        // Assert
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(activePair.Token, Is.Not.EqualTo(pendingPair.Token));
        // MFA is already enabled, so a request that passes the CSRF check ends in 409.
        Assert.That(withNewToken.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(withOldToken.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await withOldToken.Content.ReadAsStringAsync(), Does.Contain(CsrfToken.InvalidCode));
    }

    [Test]
    public async Task Verification_InvalidCode_ReturnsUnauthorizedAndKeepsSessionPending()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-invalid@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        await LoginExistingAsync("mfa-verify-invalid@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // The first verification replaces its session, so the replay comes from a second pending login.
    [Test]
    public async Task Verification_ReplayedCode_ReturnsUnauthorized()
    {
        // Arrange
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-replay@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var firstLogin = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var secondLogin = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var code = MfaTestData.CurrentCode(secret);

        // Act
        var first = await PostWithSessionAsync("/api/auth/mfa/verify", firstLogin.RawToken, new { Code = code });
        var second = await PostWithSessionAsync("/api/auth/mfa/verify", secondLogin.RawToken, new { Code = code });

        // Assert
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var replayed = await SessionTestData.GetAsync(Factory, secondLogin.Session.Id);
        Assert.That(replayed!.AuthLevel, Is.EqualTo(AuthLevel.MfaPending));
        Assert.That(replayed.RevokedAt, Is.Null);
    }

    // US-18 replay: the same code in two parallel requests (two pending logins of one user).
    [Test]
    [Category("US-18")]
    public async Task Verification_SameCodeInParallelFromTwoPendingSessions_PromotesExactlyOne()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-parallel@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var first = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var second = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var code = MfaTestData.CurrentCode(secret);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/verify", first.RawToken, new { Code = code }),
            PostWithSessionAsync("/api/auth/mfa/verify", second.RawToken, new { Code = code }));

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized), Is.EqualTo(1));
        var pendingSessions = new[]
        {
            (await SessionTestData.GetAsync(Factory, first.Session.Id))!,
            (await SessionTestData.GetAsync(Factory, second.Session.Id))!
        };
        Assert.That(pendingSessions.Count(s => s.RevokedAt is not null), Is.EqualTo(1), "only the winner is replaced");
        Assert.That(pendingSessions.All(s => s.AuthLevel == AuthLevel.MfaPending), Is.True);
        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var active = await SessionTestData.FindByTokenAsync(Factory, SessionTokenFrom(winner));
        Assert.That(active!.AuthLevel, Is.EqualTo(AuthLevel.Active));
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_OlderCodeAfterNewerOne_ReturnsUnauthorized()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-older@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var newer = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var older = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);

        var first = await PostWithSessionAsync("/api/auth/mfa/verify", newer.RawToken, new { Code = MfaTestData.NextCode(secret) });
        var second = await PostWithSessionAsync("/api/auth/mfa/verify", older.RawToken, new { Code = MfaTestData.CurrentCode(secret) });

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SessionTestData.GetAsync(Factory, older.Session.Id))!.AuthLevel,
            Is.EqualTo(AuthLevel.MfaPending));
    }

    [Test]
    public async Task Verification_ExpiredCode_ReturnsUnauthorized()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-expired@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-verify-expired@platform.com");
        var expiredCode = new Totp(Base32Encoding.ToBytes(secret))
            .ComputeTotp(DateTime.UtcNow.AddSeconds(-60));

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = expiredCode });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Verification_WithoutSession_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Verification_MissingCode_ReturnsBadRequest()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-missing@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        await LoginExistingAsync("mfa-verify-missing@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Verification_WithoutMfaEnabled_ReturnsUnauthorized()
    {
        await LoginAsAsync("mfa-verify-disabled@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static string ReadableCsrfTokenFrom(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CsrfToken.ReadableCookieName + "=", StringComparison.Ordinal))
            .Split(';')[0];
        return Uri.UnescapeDataString(cookie[(cookie.IndexOf('=') + 1)..]);
    }

    private static async Task<HttpResponseMessage> EnrollWithAsync(HttpClient client, string sessionToken, CsrfPair pair)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        pair.ApplyTo(request, sessionToken);
        return await client.SendAsync(request);
    }
}
