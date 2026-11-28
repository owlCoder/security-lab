using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Contracts.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>
/// IDN-06 (#252): the step-up before MFA enrollment. <c>POST /api/auth/mfa/enroll</c> needs the current password of
/// the session's account; a wrong one is refused like every other refusal, stores no enrollment and counts toward
/// the IDN-05 account lockout.
/// </summary>
[NonParallelizable]
[Category("IDN-06")]
public sealed class MfaStepUpIntegrationTests : MfaIntegrationTestBase
{
    private const string WrongPassword = "Wrong-Password-9!";

    // The default of Identity:Lockout; the test host does not override it.
    private static readonly int MaxFailedAccessAttempts = new IdentityLockoutOptions().MaxFailedAccessAttempts;

    [Test]
    public async Task Enroll_WithCorrectPassword_ReturnsOtpauthUri()
    {
        await LoginAsAsync("mfa-step-up-ok@platform.com");

        var response = await EnrollAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<MfaEnrollResponse>();
        Assert.That(body!.Uri, Does.StartWith("otpauth://totp/"));
    }

    [Test]
    public async Task Enroll_WithWrongPassword_Returns401_AndStoresNoEnrollment()
    {
        const string email = "mfa-step-up-wrong@platform.com";
        await LoginAsAsync(email);
        var user = await UserTestData.GetByEmailAsync(Factory, email);

        var response = await EnrollAsync(WrongPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("invalid_mfa").And.Not.Contain(WrongPassword));
        Assert.That(await GetCredentialsAsync(user!.Id), Is.Null, "no pending enrollment is stored");
        Assert.That((await UserTestData.GetAsync(Factory, user.Id))!.FailedLoginCount, Is.EqualTo(1),
            "the wrong password is charged to the IDN-05 counter");
    }

    [Test]
    public async Task Enroll_WithoutPassword_Returns400_AndChargesNoAttempt()
    {
        const string email = "mfa-step-up-missing@platform.com";
        await LoginAsAsync(email);
        var user = await UserTestData.GetByEmailAsync(Factory, email);

        var response = await EnrollAsync(password: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await GetCredentialsAsync(user!.Id), Is.Null);
        Assert.That((await UserTestData.GetAsync(Factory, user.Id))!.FailedLoginCount, Is.Zero);
    }

    [Test]
    [Category("IDN-05")]
    public async Task Enroll_RepeatedWrongPasswords_LockTheAccount_ThenEnrollAndLoginRefuseTheCorrectPassword()
    {
        const string email = "mfa-step-up-lockout@platform.com";
        await LoginAsAsync(email);
        var user = await UserTestData.GetByEmailAsync(Factory, email);

        for (var attempt = 0; attempt < MaxFailedAccessAttempts; attempt++)
            Assert.That((await EnrollAsync(WrongPassword)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That((await UserTestData.GetAsync(Factory, user!.Id))!.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));

        // The cookie still works, but a locked account gets the same 401 even with the correct password.
        var enroll = await EnrollAsync();
        Assert.That(enroll.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await GetCredentialsAsync(user.Id), Is.Null);

        // It is the lockout login reads.
        Assert.That((await LoginAsync(email)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    [Category("US-23")]
    public async Task Enroll_Refusals_AreAudited_WithTheirReason_AndWithoutThePassword()
    {
        const string email = "mfa-step-up-audit@platform.com";
        var user = await UserTestData.AddAsync(Factory, email, Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);

        // #113: the shared factory replaces ITrustedAuditWriter with a fake that stores nothing. This test reads
        // the real rows, so it restores the real writer; ConnectionStrings:Audit is the test database.
        using var audited = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        }));
        using var client = audited.CreateCsrfClient();

        for (var attempt = 0; attempt < MaxFailedAccessAttempts; attempt++)
            await PostWithSessionAsync("/api/auth/mfa/enroll", session.RawToken, new { Password = WrongPassword }, client);
        // The account is locked now, so even the correct password is refused as account_locked.
        var locked = await PostWithSessionAsync("/api/auth/mfa/enroll", session.RawToken, new { Password }, client);

        Assert.That(locked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var rows = await AuditTestData.RowsAsync(user.Id, AuditEventTypes.AuthMfaEnrollmentStarted);
        Assert.That(rows, Has.Count.EqualTo(MaxFailedAccessAttempts + 1));
        Assert.That(rows.Count(row => row.Raw.Contains("invalid_password")), Is.GreaterThanOrEqualTo(1));
        Assert.That(rows.Count(row => row.Raw.Contains("account_locked")), Is.GreaterThanOrEqualTo(1));
        foreach (var row in rows)
        {
            Assert.That(row.Outcome, Is.EqualTo("Failure"));
            Assert.That(row.ActorKind, Is.EqualTo("User"));
            Assert.That(row.ActorId, Is.EqualTo(user.Id));
            // AUD-03: neither password, nor the e-mail, is in the record.
            Assert.That(row.Raw, Does.Not.Contain(WrongPassword).And.Not.Contain(Password).And.Not.Contain(email));
        }
    }
}
