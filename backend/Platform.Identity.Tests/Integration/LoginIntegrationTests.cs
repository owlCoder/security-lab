using System.Net;
using System.Net.Http.Json;
using System.Text;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

public class LoginIntegrationTests : IntegrationTestBase
{
    [Test]
    [TestCase("", "")]
    [TestCase(" ", "password")]
    public async Task Login_WithInvalidRequest_ShouldReturnBadRequest(string email, string password)
    {
        // 1. ARRANGE
        const string rawPassword = "CorrectPassword123!";
        await UserTestData.AddAsync(Factory, "correct@email.com", rawPassword);

        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_WithMissingFields_ShouldReturnBadRequest()
    {
        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_WithMalformedJson_ShouldReturnBadRequest()
    {
        // 1. ARRANGE
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        // 2. ACT
        var response = await Client.PostAsync("/api/auth/login", content);

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_WithOverlongEmail_ShouldReturnBadRequest()
    {
        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = new string('a', 321),
            Password = "CorrectPassword123!"
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_WithOverlongPassword_ShouldReturnBadRequest()
    {
        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "correct@email.com",
            Password = new string('a', 257)
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    
    [Test]
    [TestCase("correct@email.com","badPassword")]
    [TestCase("bad@email.com","CorrectPassword123!")]
    public async Task Login_WithInvalidCredentials_ShouldReturnUnauthorized(string email, string password)
    {
        // 1. ARRANGE
        const string rawPassword = "CorrectPassword123!";
        await UserTestData.AddAsync(Factory, "correct@email.com", rawPassword);

        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task Login_WithInactiveAccount_ShouldReturnUnauthorized(AccountStatus status)
    {
        // 1. ARRANGE
        const string rawPassword = "CorrectPassword123!";
        await UserTestData.AddAsync(Factory, "inactive@email.com", rawPassword, status);

        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "inactive@email.com",
            Password = rawPassword
        });
        
        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.TryGetValues("Set-Cookie", out _), Is.False);
    }

    [Test]
    public async Task Login_WithAccountWithoutPassword_ShouldReturnUnauthorized()
    {
        // 1. ARRANGE
        await InsertAsync(new UserAccount(Guid.NewGuid(), "no-password@email.com"));

        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "no-password@email.com",
            Password = "AnyPassword123!"
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task Login_WithValidCredentials_ShouldSetSessionCookieAndReturnUserId()
    {
        // 1. ARRANGE
        const string rawPassword = "SecurePassword123!";
        var testUser = await UserTestData.AddAsync(Factory, "test@platform.com", rawPassword);

        // 2. ACT
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "test@platform.com",
            Password = rawPassword
        });

        // 3. ASSERT
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Provera JSON odgovora
        var responseBody = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(responseBody, Is.Not.Null);
        Assert.That(responseBody!.UserId, Is.EqualTo(testUser.Id));

        // Provera da li je kuki uspešno postavljen u zaglavlju odgovora
        var hasCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies);
        Assert.That(hasCookie, Is.True, "Login mora da postavi kuki sa sesijom.");
        
        // Provera da li kuki sadrži ime sesije
        var enumerable = (cookies ?? []).ToList();
        Assert.That(enumerable, Is.Not.Null);
        var allCookies = string.Join(";", enumerable);
        Assert.That(allCookies, Does.Contain(SessionCookie.Name));
        // No-store cache politika stoji u response
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        // Cookie sadrzi sva polja
        Assert.That(allCookies, Does.Contain("httponly"));
        Assert.That(allCookies, Does.Contain("samesite=strict"));
        Assert.That(allCookies, Does.Contain("secure"));
        Assert.That(allCookies, Does.Contain("path=/"));
        // Response ne sadrzi sifru
        Assert.That(responseBody.ToString(), Does.Not.Contain(rawPassword));
    }
}