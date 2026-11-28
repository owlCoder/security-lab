using System.Net;
using System.Net.Http.Json;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06 US-17 and US-18 end to end: enroll, confirm, log out, log in again and verify.</summary>
[NonParallelizable]
public sealed class MfaFlowIntegrationTests : MfaIntegrationTestBase
{
    [Test]
    public async Task EnrollmentAndVerification_ValidFlow_ReturnsActiveSession()
    {
        await LoginAsAsync("mfa-full-flow@platform.com");
        var secret = await EnrollAndReadSecretAsync();
        var confirm = await Client.PostAsJsonAsync(
            "/api/auth/mfa/enroll/confirm", new { Code = MfaTestData.CurrentCode(secret) });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logout = await Client.PostAsync("/api/auth/logout", null);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        ClearSessionCookie();
        var login = await LoginAsync("mfa-full-flow@platform.com");
        SetSessionCookie(login);

        var pending = await Client.GetAsync("/api/auth/me");
        var verify = await Client.PostAsJsonAsync(
            "/api/auth/mfa/verify", new { Code = MfaTestData.NextCode(secret) });
        SetSessionCookie(verify);
        var active = await Client.GetAsync("/api/auth/me");

        Assert.That(pending.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(active.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task EnrollmentAndVerification_InvalidCode_ReturnsUnauthorized()
    {
        await LoginAsAsync("mfa-full-flow-invalid@platform.com");
        var secret = await EnrollAndReadSecretAsync();
        var confirm = await Client.PostAsJsonAsync(
            "/api/auth/mfa/enroll/confirm", new { Code = MfaTestData.CurrentCode(secret) });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await Client.PostAsync("/api/auth/logout", null);
        ClearSessionCookie();
        var login = await LoginAsync("mfa-full-flow-invalid@platform.com");
        SetSessionCookie(login);

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
