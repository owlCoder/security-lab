using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06 US-17/US-18 audit (AUD-01, AUD-03): a success is committed only with its Audit record, and the record holds no code or secret.</summary>
[NonParallelizable]
public sealed class MfaAuditIntegrationTests : MfaIntegrationTestBase
{
    [Test]
    [Category("US-17")]
    public async Task EnrollConfirm_WhenAuditIsUnavailable_Returns503_AndMfaStaysDisabled()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-confirm-audit-down@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, secret, DateTimeOffset.UtcNow.AddMinutes(5));
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        using var failing = WithUnavailableTransactionalAudit();
        using var client = failing.CreateCsrfClient();

        var response = await PostWithSessionAsync(
            "/api/auth/mfa/enroll/confirm", session.RawToken, new { Code = MfaTestData.CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaEnabled, Is.False);
        Assert.That(credentials.PendingTotpSecretCiphertext, Is.Not.Null, "the enrollment can still be confirmed");
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.enabled"), Is.Empty);
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
        using var client = failing.CreateCsrfClient();

        var response = await PostWithSessionAsync(
            "/api/auth/mfa/verify", session.RawToken, new { Code = MfaTestData.CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await SessionTestData.GetAsync(Factory, session.Session.Id))!.AuthLevel,
            Is.EqualTo(AuthLevel.MfaPending));
        Assert.That((await GetCredentialsAsync(user.Id))!.LastTotpTimeStep, Is.Null, "the step is not consumed");
        Assert.That(await AuditTestData.RowsAsync(user.Id, "auth.mfa.verified"), Is.Empty);
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
        var confirmCode = MfaTestData.CurrentCode(secret);
        var confirm = await Client.PostAsJsonAsync("/api/auth/mfa/enroll/confirm", new { Code = confirmCode });
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await Client.PostAsync("/api/auth/logout", null);
        ClearSessionCookie();
        SetSessionCookie(await LoginAsync(email));
        var verifyCode = MfaTestData.NextCode(secret);
        var verify = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = verifyCode });
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var enabled = (await AuditTestData.RowsAsync(user!.Id, "auth.mfa.enabled")).Single();
        var verified = (await AuditTestData.RowsAsync(user.Id, "auth.mfa.verified")).Single();
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

    // The transactional Audit writer never records, so the change has no record and must not be committed.
    private static WebApplicationFactory<Program> WithUnavailableTransactionalAudit()
    {
        var audit = new Mock<ITransactionalAuditWriter>(MockBehavior.Strict);
        audit.Setup(writer => writer.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<DbTransaction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuditWriteResult.Unavailable());
        return Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITransactionalAuditWriter>();
            services.AddSingleton(audit.Object);
        }));
    }
}
