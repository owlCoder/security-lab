using System.Net;
using System.Net.Http.Json;
using System.Web;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06: HTTP steps and session cookie handling shared by the MFA integration tests.</summary>
public abstract class MfaIntegrationTestBase : IntegrationTestBase
{
    protected const string Password = "SecurePassword123!";

    // The defaults of Identity:Mfa:Lockout; the test host does not override them.
    protected static readonly int MaxFailedAttempts = new MfaLockoutOptions().MaxFailedAttempts;
    protected static readonly int LockoutMinutes = new MfaLockoutOptions().LockoutMinutes;

    protected static async Task<HttpResponseMessage> PostWithSessionAsync(
        string url, string rawToken, object body, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
        return await (client ?? Client).SendAsync(request);
    }

    protected static async Task<HttpResponseMessage> GetWithSessionAsync(string path, string rawToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
        return await Client.SendAsync(request);
    }

    protected static async Task<HttpResponseMessage> LoginAsync(string email) =>
        await Client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password });

    /// <summary>Creates the account and signs in; the client then carries its session cookie.</summary>
    protected static async Task LoginAsAsync(string email)
    {
        await UserTestData.AddAsync(Factory, email, Password);
        await LoginExistingAsync(email);
    }

    protected static async Task LoginExistingAsync(string email)
    {
        var response = await LoginAsync(email);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        SetSessionCookie(response);
    }

    /// <summary>Starts an enrollment with the client's session and returns the secret from the returned URI.</summary>
    protected static async Task<string> EnrollAndReadSecretAsync()
    {
        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        Assert.That(body, Is.Not.Null);
        var secret = HttpUtility.ParseQueryString(new Uri(body!.Uri).Query)["secret"];
        Assert.That(secret, Is.Not.Null.And.Not.Empty);
        return secret!;
    }

    protected static async Task<MfaCredentials?> GetCredentialsAsync(Guid userId) =>
        await GetAsync<MfaCredentials>(userId);

    protected static async Task<Session?> GetCurrentSessionAsync() =>
        await SessionTestData.FindByTokenAsync(Factory, CurrentSessionToken());

    protected static void SetSessionCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    protected static void SetSessionCookie(string rawToken)
    {
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
    }

    protected static void ClearSessionCookie() => Client.DefaultRequestHeaders.Remove("Cookie");

    protected static string CurrentSessionToken()
    {
        var cookie = Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        return cookie[(cookie.IndexOf('=') + 1)..];
    }

    protected static string SessionTokenFrom(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal))
            .Split(';')[0];
        return cookie[(cookie.IndexOf('=') + 1)..];
    }
}
