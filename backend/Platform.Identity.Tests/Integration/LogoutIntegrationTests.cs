using System.Net;
using Platform.Api.Services.Identity;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

public class LogoutIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Logout_WithoutSession_ShouldReturnNoContent()
    {
        // 2.Act
        var response = await Client.PostAsync("/api/auth/logout", null);
        
        // 3.Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logout_WithValidSession_ShouldRevokeSession()
    {
        // 1. ARRANGE
        const string rawPassword = "SecurePassword123!";
        var testUser = await UserTestData.AddAsync(Factory, "test@platform.com", rawPassword);
        var issued = await SessionTestData.CreateAsync(Factory, testUser.Id);

        // 2. ACT
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");
        var response = await Client.PostAsync("/api/auth/logout", null);
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var session = await SessionTestData.GetAsync(Factory, issued.Session.Id);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.RevokedAt, Is.Not.Null);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies), Is.True);
        Assert.That(string.Join(";", cookies!), Does.Contain(SessionCookie.Name));
    }

    [Test]
    public async Task Logout_WhenMultipleSessions_ShouldRevokeOnlyTargetSession()
    {
        // 1. ARRANGE
        const string rawPassword = "SecurePassword123!";
        var testUser = await UserTestData.AddAsync(Factory, "test@platform.com", rawPassword);
        var issuedFirst = await SessionTestData.CreateAsync(Factory, testUser.Id);
        var issuedSecond = await SessionTestData.CreateAsync(Factory, testUser.Id);
        var issuedThird = await SessionTestData.CreateAsync(Factory, testUser.Id);

        // 2. ACT
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issuedSecond.RawToken}");
        var response = await Client.PostAsync("/api/auth/logout", null);
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        
        // First session is not revoked
        var sessionFirst = await SessionTestData.GetAsync(Factory, issuedFirst.Session.Id);
        Assert.That(sessionFirst, Is.Not.Null);
        Assert.That(sessionFirst!.RevokedAt, Is.Null);
        
        // Second session is revoked
        var sessionSecond = await SessionTestData.GetAsync(Factory, issuedSecond.Session.Id);
        Assert.That(sessionSecond, Is.Not.Null);
        Assert.That(sessionSecond!.RevokedAt, Is.Not.Null);
        
        // Thord session is not revoked
        var sessionThird = await SessionTestData.GetAsync(Factory, issuedThird.Session.Id);
        Assert.That(sessionThird, Is.Not.Null);
        Assert.That(sessionThird!.RevokedAt, Is.Null);
    }

    [Test]
    public async Task Logout_WithUnknownToken_ShouldReturnNoContent()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "test@platform.com", "SecurePassword123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}=unknown-token");

        // 2. ACT
        var response = await Client.PostAsync("/api/auth/logout", null);
        var session = await SessionTestData.GetAsync(Factory, issued.Session.Id);

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(session!.RevokedAt, Is.Null);
    }

    [Test]
    public async Task Logout_WithAlreadyRevokedSession_ShouldRemainIdempotent()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "test@platform.com", "SecurePassword123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");

        // 2. ACT
        var firstResponse = await Client.PostAsync("/api/auth/logout", null);
        var secondResponse = await Client.PostAsync("/api/auth/logout", null);
        var session = await SessionTestData.GetAsync(Factory, issued.Session.Id);

        // 3. ASSERT
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(session!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task Logout_WithExpiredSession_ShouldReturnNoContentWithoutRevokingSession()
    {
        // 1. ARRANGE
        var user = await UserTestData.AddAsync(Factory, "test@platform.com", "SecurePassword123!");
        var issued = await SessionTestData.CreateAsync(Factory, user.Id, lifetime: TimeSpan.FromMinutes(-1));
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={issued.RawToken}");

        // 2. ACT
        var response = await Client.PostAsync("/api/auth/logout", null);
        var session = await SessionTestData.GetAsync(Factory, issued.Session.Id);

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(session!.RevokedAt, Is.Null);
    }

    [Test]
    public async Task Logout_ShouldNotRevokeAnotherUsersSession()
    {
        // 1. ARRANGE
        var firstUser = await UserTestData.AddAsync(Factory, "first@platform.com", "SecurePassword123!");
        var secondUser = await UserTestData.AddAsync(Factory, "second@platform.com", "SecurePassword123!");
        var firstSession = await SessionTestData.CreateAsync(Factory, firstUser.Id);
        var secondSession = await SessionTestData.CreateAsync(Factory, secondUser.Id);
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={firstSession.RawToken}");

        // 2. ACT
        var response = await Client.PostAsync("/api/auth/logout", null);
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var otherSession = await SessionTestData.GetAsync(Factory, secondSession.Session.Id);
        Assert.That(otherSession!.RevokedAt, Is.Null);
    }
}
