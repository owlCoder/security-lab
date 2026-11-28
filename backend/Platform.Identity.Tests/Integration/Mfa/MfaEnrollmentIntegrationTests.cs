using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Contracts.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06 US-17: enrollment (POST /api/auth/mfa/enroll) and its confirmation (/enroll/confirm), including parallel requests.</summary>
[NonParallelizable]
public sealed class MfaEnrollmentIntegrationTests : MfaIntegrationTestBase
{
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
    [Category("US-17")]
    public async Task Enroll_TwoParallelFirstEnrollments_OneSucceeds_OneConflicts_NoServerError()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-parallel@platform.com", Password);
        var first = await SessionTestData.CreateAsync(Factory, user.Id);
        var second = await SessionTestData.CreateAsync(Factory, user.Id);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/enroll", first.RawToken, new { }),
            PostWithSessionAsync("/api/auth/mfa/enroll", second.RawToken, new { }));

        Assert.That(responses.Select(r => r.StatusCode),
            Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await AssertStoredSecretMatchesTheReturnedUriAsync(user.Id, responses);
    }

    [Test]
    [Category("US-17")]
    public async Task Enroll_TwoParallelEnrollmentsOverAnExpiredOne_OneSucceeds_OneConflicts()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-enroll-parallel-expired@platform.com", Password);
        await MfaTestData.AddPendingAsync(
            Factory, user.Id, TotpHelpers.GenerateSecret(), DateTimeOffset.UtcNow.AddMinutes(-1));
        var first = await SessionTestData.CreateAsync(Factory, user.Id);
        var second = await SessionTestData.CreateAsync(Factory, user.Id);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/enroll", first.RawToken, new { }),
            PostWithSessionAsync("/api/auth/mfa/enroll", second.RawToken, new { }));

        Assert.That(responses.Select(r => r.StatusCode),
            Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await AssertStoredSecretMatchesTheReturnedUriAsync(user.Id, responses);
    }

    [Test]
    public async Task EnrollConfirm_ValidCode_EnablesMfa()
    {
        await LoginAsAsync("mfa-confirm@platform.com");
        var secret = await EnrollAndReadSecretAsync();
        var code = MfaTestData.CurrentCode(secret);

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
    [Category("US-17")]
    public async Task EnrollConfirm_WithoutEnrollment_ReturnsUnauthorized()
    {
        await LoginAsAsync("mfa-confirm-no-enroll@platform.com");

        var response = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = "123456" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_mfa"));
    }

    [Test]
    [Category("US-17")]
    public async Task EnrollConfirm_TwoParallelConfirmations_EnableMfaOnce()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-parallel@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var first = await SessionTestData.CreateAsync(Factory, user.Id);
        var second = await SessionTestData.CreateAsync(Factory, user.Id);
        var code = MfaTestData.CurrentCode(secret);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/enroll/confirm", first.RawToken, new { Code = code }),
            PostWithSessionAsync("/api/auth/mfa/enroll/confirm", second.RawToken, new { Code = code }));

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaEnabled, Is.True);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Null);
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

    // The URI the user got is the enrollment that is stored, so the code from the app can confirm it.
    private static async Task AssertStoredSecretMatchesTheReturnedUriAsync(Guid userId, HttpResponseMessage[] responses)
    {
        var ok = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        var secret = HttpUtility.ParseQueryString(new Uri(body!.Uri).Query)["secret"]!;
        var credentials = await GetAsync<MfaCredentials>(userId);
        using var scope = Factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<ITotpSecretProtector>();
        Assert.That(protector.Unprotect(credentials!.PendingTotpSecretCiphertext!), Is.EqualTo(secret));
    }
}
