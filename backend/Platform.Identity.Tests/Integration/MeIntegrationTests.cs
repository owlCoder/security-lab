using System.Net;
using System.Net.Http.Json;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

public class MeIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Me_WithoutSession_ShouldReturnUnauthorized()
    {
        // 2. ACT
        var response = await Client.GetAsync("/api/auth/me");
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WithValidSession_ShouldReturnOk()
    {
        // 1. ARRANGE
        var testUser = await UserTestData.AddAsync(Factory, "test@example.com", "Password123!");
        var issued = await SessionTestData.CreateAsync(Factory, testUser.Id);
        
        // 2. ACT
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");
        var response = await Client.GetAsync("/api/auth/me");
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var responseBody = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.That(responseBody, Is.Not.Null);
        Assert.That(responseBody!.UserId, Is.EqualTo(testUser.Id));
        Assert.That(responseBody.Level, Is.EqualTo(issued.Session.AuthLevel.ToString()));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task Me_WithUnknownToken_ShouldReturnUnauthorized()
    {
        // 1. ARRANGE
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}=unknown-token");
        
        // 2. ACT
        var response = await Client.GetAsync("/api/auth/me");

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WithExpiredSession_ShouldReturnUnauthorized()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "expired@example.com", "Password123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id, lifetime: TimeSpan.FromMinutes(-1));
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");

        // 2. ACT
        var response = await Client.GetAsync("/api/auth/me");

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WithRevokedSession_ShouldReturnUnauthorized()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "revoked@example.com", "Password123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");
        await Client.PostAsync("/api/auth/logout", null);

        // 2. ACT
        var response = await Client.GetAsync("/api/auth/me");

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WithMfaPendingSession_ShouldReturnUnauthorized()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "mfa@example.com", "Password123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");

        // 2. ACT
        var response = await Client.GetAsync("/api/auth/me");

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}