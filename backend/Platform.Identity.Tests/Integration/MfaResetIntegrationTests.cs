using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// US-32 / IDN-06: administrator MFA reset through the real host and PostgreSQL.
/// The MFA service is replaced by FakeMfaResetService (the real one does not exist yet), but session
/// revocation goes through the real SessionManager, so the sessions of the target really stop working.
/// The audit writer is replaced by a capturing writer, so the tests can read the event. Pattern copied from
/// AccountUnlockIntegrationTests.
/// </summary>
[Category("IDN-06")]
public class MfaResetIntegrationTests : IntegrationTestBase
{
    // Must be an id in Stub:AdminIds of appsettings.Development.json.
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Password = "Password123!";
    private const string Reason = "lost_device";

    [Test]
    public async Task Admin_ResetsStudentMfa_Returns200_AllStudentSessionsRevoked_AndIsAudited()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);
        using var host = HostWithFake(out var client, out var fake, out var audit, mfaOnStudent: true, student.Id, admin.Id);

        var response = await SendAsync(ResetUrl(student.Id), adminSession.RawToken, new { reason = Reason }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<ResetResponse>();
        Assert.That(body!.Id, Is.EqualTo(student.Id));
        Assert.That(body.MfaReset, Is.True);
        Assert.That(body.MfaWasEnabled, Is.True);
        Assert.That(body.SessionsRevoked, Is.EqualTo(1));
        Assert.That(fake.IsMfaEnabled(student.Id), Is.False);
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Not.Null,
            "the old session of the target no longer works");
        Assert.That((await SessionTestData.GetAsync(Factory, adminSession.Session.Id))!.RevokedAt, Is.Null,
            "the administrator's own session stays valid");

        var written = audit.Written.Single(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset);
        Assert.That(written.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(written.Draft.Resource!.Id, Is.EqualTo(student.Id.ToString("D")));
        Assert.That(written.Draft.Details!["resetReason"], Is.EqualTo(Reason));
        Assert.That(written.Draft.Details["mfaWasEnabled"], Is.EqualTo("true"));
        Assert.That(written.Draft.Details["sessionsRevoked"], Is.EqualTo("1"));
        Assert.That(written.Draft.Details.Keys, Is.SupersetOf(new[] { "resetReason", "mfaWasEnabled", "sessionsRevoked" }));
        Assert.That(written.Draft.Details.Values, Has.None.Contain(student.Email), "no e-mail in the audit event");
    }

    [Test]
    public async Task Admin_ResetsAccountWithoutMfa_Returns200_MfaWasEnabledFalse()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        using var host = HostWithFake(out var client, out _, out var audit, mfaOnStudent: false, student.Id, admin.Id);

        var response = await SendAsync(ResetUrl(student.Id), adminSession.RawToken, new { reason = "other" }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await response.Content.ReadFromJsonAsync<ResetResponse>())!.MfaWasEnabled, Is.False);
        Assert.That(audit.Written.Single(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset)
            .Draft.Details!["mfaWasEnabled"], Is.EqualTo("false"));
    }

    [Test]
    public async Task Student_CannotReset_Returns403_NothingChanges_NoResetEventFromTheUseCase()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);
        var otherSession = await SessionTestData.CreateAsync(Factory, other.Id);
        using var host = HostWithFake(out var client, out var fake, out var audit, mfaOnStudent: true, other.Id, admin.Id);

        var response = await SendAsync(ResetUrl(other.Id), studentSession.RawToken, new { reason = "other" }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(fake.IsMfaEnabled(other.Id), Is.True);
        Assert.That((await SessionTestData.GetAsync(Factory, otherSession.Session.Id))!.RevokedAt, Is.Null);
        // ACC-05: the central Access middleware denies before the handler runs (and audits access.denied itself).
        Assert.That(audit.Written.Where(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset), Is.Empty);
    }

    [Test]
    public async Task WithoutSession_Returns401SessionInvalid_NothingChanges()
    {
        var (_, student) = await ArrangeAdminAndStudentAsync();
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        var response = await Client.PostAsJsonAsync(ResetUrl(student.Id), new { reason = "other" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("session_invalid"));
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Null);
    }

    // A stale session: an unknown token, or the administrator's own session after it was revoked.
    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidSession_Returns401SessionInvalid_ClearsCookie(bool revokedAdminSession)
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var token = "not-a-session-token";
        if (revokedAdminSession)
        {
            token = (await SessionTestData.CreateAsync(Factory, admin.Id)).RawToken;
            await RevokeSessionsAsync(admin.Id);
        }

        var response = await SendAsync(ResetUrl(student.Id), token, new { reason = Reason });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("session_invalid"));
        var cleared = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal) && c.Contains("1970"));
        Assert.That(cleared, Is.True, "the rejected cookie is expired");
    }

    [Test]
    public async Task UnknownAccount_Returns404_AndFailureIsAudited()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var unknown = Guid.NewGuid();
        using var host = HostWithFake(out var client, out _, out var audit, mfaOnStudent: true, student.Id, admin.Id);

        var response = await SendAsync(ResetUrl(unknown), adminSession.RawToken, new { reason = Reason }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("account_not_found"));
        var written = audit.Written.Single(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset);
        Assert.That(written.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(written.Draft.Details!["reasonCode"], Is.EqualTo("account_not_found"));
        Assert.That(written.Draft.Details.ContainsKey("resetReason"), Is.False);
    }

    [Test]
    public async Task Admin_CannotResetOwnMfa_Returns409_MfaStays_AndFailureIsAudited()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        using var host = HostWithFake(out var client, out var fake, out var audit, mfaOnStudent: true, student.Id, admin.Id);

        var response = await SendAsync(ResetUrl(admin.Id), adminSession.RawToken, new { reason = Reason }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("self_change_not_allowed"));
        Assert.That(fake.IsMfaEnabled(admin.Id), Is.True);
        Assert.That((await SessionTestData.GetAsync(Factory, adminSession.Session.Id))!.RevokedAt, Is.Null);
        var written = audit.Written.Single(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset);
        Assert.That(written.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(written.Draft.Details!["reasonCode"], Is.EqualTo("self_change"));
    }

    // The reason is a code from a fixed list: missing, blank, free text, wrong case or unknown codes are refused.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("two\nlines")]
    [TestCase("lost phone")]
    [TestCase("LOST_DEVICE")]
    [TestCase("not_in_the_list")]
    public async Task MissingOrInvalidReason_Returns400_NothingChanges_AndFailureIsAudited(string? reason)
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);
        using var host = HostWithFake(out var client, out var fake, out var audit, mfaOnStudent: true, student.Id, admin.Id);

        var response = await SendAsync(ResetUrl(student.Id), adminSession.RawToken, new { reason }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_reason"));
        Assert.That(fake.IsMfaEnabled(student.Id), Is.True);
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Null);
        var written = audit.Written.Single(w => w.Draft.EventType == AuditEventTypes.AccountMfaReset);
        Assert.That(written.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(written.Draft.Details!["reasonCode"], Is.EqualTo("invalid_reason"));
        Assert.That(written.Draft.Details.Values, Has.None.Contain("phone"), "the rejected text is never audited");
    }

    [Test]
    public async Task NoRealMfaService_DefaultsToUnavailable_Returns503_NothingChanges()
    {
        // The default registration (UnavailableMfaResetService) is what runs until the MFA service is registered.
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        var response = await SendAsync(ResetUrl(student.Id), adminSession.RawToken, new { reason = Reason });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Null);
    }

    // ---- helpers ----

    private sealed record ResetResponse(Guid Id, bool MfaReset, bool MfaWasEnabled, int SessionsRevoked);

    /// <summary>
    /// Host where IMfaResetService is the fake and the trusted audit writer captures events. Session revocation uses
    /// the real SessionManager, so it hits the real database.
    /// </summary>
    private static IDisposable HostWithFake(
        out HttpClient client, out FakeMfaResetService fake, out CapturingAuditWriter audit,
        bool mfaOnStudent, Guid studentId, Guid adminId)
    {
        var service = new FakeMfaResetService(async (userId, ct) =>
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            return await new SessionManager(db).RevokeAllForUser(userId, "mfa reset", ct);
        });
        service.AddUser(adminId, isAdministrator: true, mfaEnabled: true);
        service.AddUser(studentId, isAdministrator: false, mfaEnabled: mfaOnStudent);
        var writer = new CapturingAuditWriter();

        var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMfaResetService>();
            services.AddSingleton<IMfaResetService>(service);
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddSingleton<ITrustedAuditWriter>(writer);
        }));
        client = host.CreateClient();
        fake = service;
        audit = writer;
        return host;
    }

    private static string ResetUrl(Guid accountId) => $"/api/admin/accounts/{accountId}/mfa/reset";

    private static async Task<(UserAccount Admin, UserAccount Student)> ArrangeAdminAndStudentAsync()
    {
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        return (admin, student);
    }

    private static async Task<HttpResponseMessage> SendAsync(string url, string token, object body, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        request.Content = JsonContent.Create(body);
        return await (client ?? Client).SendAsync(request);
    }

    private static async Task RevokeSessionsAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        Assert.That(await new SessionManager(db).RevokeAllForUser(userId, "test"), Is.EqualTo(1));
    }
}
