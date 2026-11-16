using System.Net;
using System.Net.Http.Json;
using System.Web;
using OtpNet;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Helpers;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

[NonParallelizable]
public sealed class MfaIntegrationTests : IntegrationTestBase
{
    private const string Password = "SecurePassword123!";

    [Test]
    public async Task Enroll_ActiveSession_ReturnsEnrollmentUri()
    {
        await LoginAsAsync("mfa-enroll@platform.com");

        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Uri, Does.StartWith("otpauth://totp/"));

        var user = await UserTestData.GetByEmailAsync(Factory, "mfa-enroll@platform.com");
        Assert.That(user, Is.Not.Null);
        var credentials = await GetCredentialsAsync(user!.Id);
        Assert.That(credentials, Is.Not.Null);
        Assert.That(credentials!.MfaEnabled, Is.False);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Not.Null);
    }

    [Test]
    public async Task Enroll_WithoutSession_ReturnsUnauthorized()
    {
        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Enroll_AlreadyEnabled_ReturnsConflict()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-enabled@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        SetSessionCookie(session.RawToken);

        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Enroll_MfaPendingSession_ReturnsUnauthorized()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-pending@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        await LoginExistingAsync("mfa-enroll-pending@platform.com");

        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Enroll_RepeatedBeforeConfirmation_ReturnsConflict()
    {
        await LoginAsAsync("mfa-enroll-repeat@platform.com");
        var first = await Client.PostAsync("/api/auth/mfa/enroll", null);

        var second = await Client.PostAsync("/api/auth/mfa/enroll", null);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task EnrollConfirm_ValidCode_EnablesMfa()
    {
        await LoginAsAsync("mfa-confirm@platform.com");
        var secret = await EnrollAndReadSecretAsync();
        var code = CurrentCode(secret);

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = code });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var user = await UserTestData.GetByEmailAsync(Factory, "mfa-confirm@platform.com");
        Assert.That(user, Is.Not.Null);
        var credentials = await GetCredentialsAsync(user!.Id);
        Assert.That(credentials!.MfaEnabled, Is.True);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Null);
    }

    [Test]
    public async Task EnrollConfirm_InvalidCode_ReturnsUnauthorizedAndKeepsMfaDisabled()
    {
        await LoginAsAsync("mfa-confirm-invalid@platform.com");
        await EnrollAndReadSecretAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "000000" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var user = await UserTestData.GetByEmailAsync(Factory, "mfa-confirm-invalid@platform.com");
        Assert.That(user, Is.Not.Null);
        Assert.That((await GetCredentialsAsync(user!.Id))!.MfaEnabled, Is.False);
    }

    [Test]
    public async Task EnrollConfirm_ExpiredEnrollment_ReturnsUnauthorized()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-expired@platform.com", Password);
        await MfaTestData.AddPendingAsync(
            Factory, user.Id, TotpHelpers.GenerateSecret(), DateTimeOffset.UtcNow.AddMinutes(-1));
        await LoginExistingAsync("mfa-confirm-expired@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EnrollConfirm_WithoutSession_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EnrollConfirm_AlreadyEnabled_ReturnsUnauthorized()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-enabled@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        SetSessionCookie(session.RawToken);

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EnrollConfirm_MissingCode_ReturnsBadRequest()
    {
        await LoginAsAsync("mfa-confirm-missing@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Enrollment_WithoutConfirmation_DoesNotEnableMfa()
    {
        await LoginAsAsync("mfa-enroll-only@platform.com");
        await EnrollAndReadSecretAsync();

        var logout = await Client.PostAsync("/api/auth/logout", null);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        ClearSessionCookie();

        await LoginExistingAsync("mfa-enroll-only@platform.com");
        var me = await Client.GetAsync("/api/auth/me");

        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Verification_ValidCode_PromotesPendingSessionToActive()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-verify@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = CurrentCode(secret) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Client.GetAsync("/api/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
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

    [Test]
    public async Task Verification_ReplayedCode_ReturnsUnauthorized()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-replay@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-verify-replay@platform.com");
        var code = CurrentCode(secret);

        var first = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = code });
        var second = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = code });

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
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

    [Test]
    public async Task Verification_AfterFailedAttemptThreshold_RevokesPendingSession()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-lockout@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        await LoginExistingAsync("mfa-verify-lockout@platform.com");
        var pendingSession = await GetCurrentSessionAsync();

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.That(
                (await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" })).StatusCode,
                Is.EqualTo(HttpStatusCode.Unauthorized));

        var revokedSession = await SessionTestData.GetAsync(Factory, pendingSession!.Id);
        Assert.That(revokedSession!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task EnrollmentAndVerification_ValidFlow_ReturnsActiveSession()
    {
        await LoginAsAsync("mfa-full-flow@platform.com");
        var secret = await EnrollAndReadSecretAsync();
        var confirm = await Client.PostAsJsonAsync(
            "/api/auth/mfa/enroll/confirm", new { Code = CurrentCode(secret) });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logout = await Client.PostAsync("/api/auth/logout", null);
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        ClearSessionCookie();
        var login = await LoginAsync("mfa-full-flow@platform.com");
        SetSessionCookie(login);

        var pending = await Client.GetAsync("/api/auth/me");
        var verify = await Client.PostAsJsonAsync(
            "/api/auth/mfa/verify", new { Code = NextCode(secret) });
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
            "/api/auth/mfa/enroll/confirm", new { Code = CurrentCode(secret) });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await Client.PostAsync("/api/auth/logout", null);
        ClearSessionCookie();
        var login = await LoginAsync("mfa-full-flow-invalid@platform.com");
        SetSessionCookie(login);

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<HttpResponseMessage> LoginAsync(string email) =>
        await Client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password });

    private async Task LoginAsAsync(string email)
    {
        await UserTestData.AddAsync(Factory, email, Password);
        await LoginExistingAsync(email);
    }

    private async Task LoginExistingAsync(string email)
    {
        var response = await LoginAsync(email);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        SetSessionCookie(response);
    }

    private async Task<string> EnrollAndReadSecretAsync()
    {
        var response = await Client.PostAsync("/api/auth/mfa/enroll", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        Assert.That(body, Is.Not.Null);
        var secret = HttpUtility.ParseQueryString(new Uri(body!.Uri).Query)["secret"];
        Assert.That(secret, Is.Not.Null.And.Not.Empty);
        return secret!;
    }

    private async Task<MfaCredentials?> GetCredentialsAsync(Guid userId) =>
        await GetAsync<MfaCredentials>(userId);

    private async Task<Session?> GetCurrentSessionAsync()
    {
        var cookie = Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        var rawToken = cookie[(cookie.IndexOf('=') + 1)..];
        return await SessionTestData.FindByTokenAsync(Factory, rawToken);
    }

    private static string CurrentCode(string secret) =>
        new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow);

    private static string NextCode(string secret) =>
        new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow.AddSeconds(30));

    private static void SetSessionCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    private static void SetSessionCookie(string rawToken)
    {
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
    }

    private static void ClearSessionCookie() => Client.DefaultRequestHeaders.Remove("Cookie");
}
