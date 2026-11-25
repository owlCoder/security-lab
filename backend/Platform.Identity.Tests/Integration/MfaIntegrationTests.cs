using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OtpNet;
using Npgsql;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

[NonParallelizable]
public sealed class MfaIntegrationTests : IntegrationTestBase
{
    private const string Password = "SecurePassword123!";

    // A successful confirmation or verification is committed only together with its Audit record (same database).
    [OneTimeSetUp]
    public async Task MigrateAuditSchema() =>
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);

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

    // owlCoder review point 1: the same code in two parallel requests (two pending logins of one user).
    [Test]
    [Category("US-18")]
    public async Task Verification_SameCodeInParallelFromTwoPendingSessions_PromotesExactlyOne()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-parallel@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var first = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var second = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var code = CurrentCode(secret);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/verify", first.RawToken, new { Code = code }),
            PostWithSessionAsync("/api/auth/mfa/verify", second.RawToken, new { Code = code }));

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized), Is.EqualTo(1));
        var levels = new[]
        {
            (await SessionTestData.GetAsync(Factory, first.Session.Id))!.AuthLevel,
            (await SessionTestData.GetAsync(Factory, second.Session.Id))!.AuthLevel
        };
        Assert.That(levels, Is.EquivalentTo(new[] { AuthLevel.Active, AuthLevel.MfaPending }));
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

        var first = await PostWithSessionAsync("/api/auth/mfa/verify", newer.RawToken, new { Code = NextCode(secret) });
        var second = await PostWithSessionAsync("/api/auth/mfa/verify", older.RawToken, new { Code = CurrentCode(secret) });

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SessionTestData.GetAsync(Factory, older.Session.Id))!.AuthLevel,
            Is.EqualTo(AuthLevel.MfaPending));
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
        var code = CurrentCode(secret);

        var responses = await Task.WhenAll(
            PostWithSessionAsync("/api/auth/mfa/enroll/confirm", first.RawToken, new { Code = code }),
            PostWithSessionAsync("/api/auth/mfa/enroll/confirm", second.RawToken, new { Code = code }));

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaEnabled, Is.True);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Null);
    }

    // owlCoder review point 2: the lockout is enforced, also against a new password login and a correct code.
    [Test]
    [Category("US-18")]
    public async Task Verification_AfterThreshold_NewPasswordLoginWithCorrectCode_IsRefused()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-relogin@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-lockout-relogin@platform.com");
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
            await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" });
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaLockedUntil, Is.GreaterThan(DateTimeOffset.UtcNow));

        // The password is right, so login gives a new MfaPending session; the second factor stays locked.
        await LoginExistingAsync("mfa-lockout-relogin@platform.com");
        var pending = await GetCurrentSessionAsync();
        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = CurrentCode(secret) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_mfa"),
            "a locked account answers like a wrong code");
        var stored = await SessionTestData.GetAsync(Factory, pending!.Id);
        Assert.That(stored!.AuthLevel, Is.EqualTo(AuthLevel.MfaPending));
        Assert.That(stored.RevokedAt, Is.Not.Null, "the pending session of a locked account is revoked");
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaLockedUntil, Is.Not.Null, "a correct code does not clear the lockout");
        Assert.That(credentials.LastTotpTimeStep, Is.Null, "no step is consumed while locked");
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_AfterLockoutMinutes_CorrectCodeWorksAgain()
    {
        var clock = new ShiftedClock();
        using var shifted = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = shifted.CreateClient();
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-expiry@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var locking = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
            await PostWithSessionAsync("/api/auth/mfa/verify", locking.RawToken, new { Code = "000000" }, client);
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaLockedUntil, Is.Not.Null);

        clock.Offset = TimeSpan.FromMinutes(LockoutMinutes) + TimeSpan.FromSeconds(1);
        var fresh = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var response = await PostWithSessionAsync(
            "/api/auth/mfa/verify", fresh.RawToken, new { Code = CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SessionTestData.GetAsync(Factory, fresh.Session.Id))!.AuthLevel, Is.EqualTo(AuthLevel.Active));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaLockedUntil, Is.Null);
        Assert.That(credentials.MfaFailedAttempts, Is.Zero);
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_ParallelWrongCodes_CountEveryAttempt_AndLockAtTheThreshold()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-parallel@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());

        // One short of the threshold, all at once: no attempt may be lost.
        await Task.WhenAll(await WrongCodesInParallelAsync(user.Id, MaxFailedAttempts - 1));
        var belowThreshold = await GetCredentialsAsync(user.Id);
        Assert.That(belowThreshold!.MfaFailedAttempts, Is.EqualTo(MaxFailedAttempts - 1));
        Assert.That(belowThreshold.MfaLockedUntil, Is.Null);

        // Past the threshold: the first one that reaches it locks, the rest are refused without being counted.
        await Task.WhenAll(await WrongCodesInParallelAsync(user.Id, 3));
        var locked = await GetCredentialsAsync(user.Id);
        Assert.That(locked!.MfaFailedAttempts, Is.EqualTo(MaxFailedAttempts));
        Assert.That(locked.MfaLockedUntil, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    // owlCoder review point 3: MFA audit goes to the Audit module, atomically with the change.
    [Test]
    [Category("US-17")]
    public async Task EnrollConfirm_WhenAuditIsUnavailable_Returns503_AndMfaStaysDisabled()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-audit-down@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        using var failing = WithUnavailableTransactionalAudit();
        using var client = failing.CreateClient();

        var response = await PostWithSessionAsync(
            "/api/auth/mfa/enroll/confirm", session.RawToken, new { Code = CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaEnabled, Is.False);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Not.Null, "the enrollment can still be confirmed");
        Assert.That(await AuditRowsAsync(user.Id, "auth.mfa.enabled"), Is.Empty);
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_WhenAuditIsUnavailable_Returns503_AndSessionStaysPending()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-audit-down@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var session = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        using var failing = WithUnavailableTransactionalAudit();
        using var client = failing.CreateClient();

        var response = await PostWithSessionAsync(
            "/api/auth/mfa/verify", session.RawToken, new { Code = CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await SessionTestData.GetAsync(Factory, session.Session.Id))!.AuthLevel,
            Is.EqualTo(AuthLevel.MfaPending));
        Assert.That((await GetCredentialsAsync(user.Id))!.LastTotpTimeStep, Is.Null, "the step is not consumed");
        Assert.That(await AuditRowsAsync(user.Id, "auth.mfa.verify"), Is.Empty);
    }

    [Test]
    [Category("US-17")]
    [Category("US-18")]
    public async Task ConfirmAndVerify_AreRecordedInTheAuditModule_WithoutCodeOrSecret()
    {
        const string email = "mfa-audit-rows@platform.com";
        await LoginAsAsync(email);
        var user = await UserTestData.GetByEmailAsync(Factory, email);
        var secret = await EnrollAndReadSecretAsync();
        var confirmCode = CurrentCode(secret);
        var confirm = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = confirmCode });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await Client.PostAsync("/api/auth/logout", null);
        ClearSessionCookie();
        SetSessionCookie(await LoginAsync(email));
        var verifyCode = NextCode(secret);
        var verify = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = verifyCode });
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var enabled = (await AuditRowsAsync(user!.Id, "auth.mfa.enabled")).Single();
        var verified = (await AuditRowsAsync(user.Id, "auth.mfa.verify")).Single();
        foreach (var (row, response) in new[] { (enabled, confirm), (verified, verify) })
        {
            Assert.That(row.Outcome, Is.EqualTo("Success"));
            Assert.That(row.ActorKind, Is.EqualTo("User"));
            Assert.That(row.ActorId, Is.EqualTo(user.Id));
            Assert.That(row.CorrelationId, Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
            // AUD-03: no code, no secret, no e-mail in the record.
            Assert.That(row.Raw, Does.Not.Contain(confirmCode).And.Not.Contain(verifyCode)
                .And.Not.Contain(secret).And.Not.Contain(email));
        }
    }

    private static WebApplicationFactory<Program> WithUnavailableTransactionalAudit() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITransactionalAuditWriter>();
            services.AddSingleton<ITransactionalAuditWriter, UnavailableTransactionalAuditWriter>();
        }));

    private sealed class UnavailableTransactionalAuditWriter : ITransactionalAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            DbTransaction transaction, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditWriteResult.Unavailable());
    }

    private sealed record AuditRow(string Outcome, string ActorKind, Guid? ActorId, string CorrelationId, string Raw);

    private static async Task<List<AuditRow>> AuditRowsAsync(Guid userId, string eventType)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT outcome, actor_kind, actor_id, correlation_id, row_to_json(e)::text
            FROM audit.audit_events e
            WHERE event_type = @type AND resource_id = @id
            """, connection);
        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("id", userId.ToString());
        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new AuditRow(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    // milanPR15 review: enrollment edge cases.
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

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private static async Task<IEnumerable<Task<HttpResponseMessage>>> WrongCodesInParallelAsync(Guid userId, int count)
    {
        var tokens = new List<string>();
        for (var i = 0; i < count; i++)
            tokens.Add((await SessionTestData.CreateAsync(Factory, userId, AuthLevel.MfaPending)).RawToken);
        return tokens
            .Select(token => PostWithSessionAsync("/api/auth/mfa/verify", token, new { Code = "000000" }))
            .ToList();
    }

    private sealed class ShiftedClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }

    private static async Task<HttpResponseMessage> PostWithSessionAsync(
        string url, string rawToken, object body, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
        return await (client ?? Client).SendAsync(request);
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
