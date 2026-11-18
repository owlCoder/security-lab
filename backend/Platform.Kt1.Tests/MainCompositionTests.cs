using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Platform.Api.Modules.Kt1;

namespace Platform.Kt1.Tests;

[TestFixture]
public class MainCompositionTests
{
    private static readonly Guid MainAdmin = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MainTarget = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Test]
    public async Task EnablingKt1PreservesMainAccountStatusEndpointAndItsAuthorization()
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await client.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var path = $"/api/admin/accounts/{MainTarget}/status";
        // A synthetic course-admin cookie is not an identity-module administrator session.
        Assert.That((await client.PutAsJsonAsync(path, new { status = "BLOCKED", reason = "Integration check" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        // IDN-02 replaced the Development X-Stub-User identity stub with the server-side session:
        // the header alone no longer authenticates the identity-module administrator.
        client.DefaultRequestHeaders.Add("X-Stub-User", MainAdmin.ToString());
        Assert.That((await client.PutAsJsonAsync(path, new { status = "BLOCKED", reason = "Integration check" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Services.GetRequiredService<DemoStore>().FindAccount(DemoStore.Ana).Value!.IsActive, Is.True);
    }

    [Test]
    public async Task MainDevelopmentIdentityHeaderDoesNotAuthenticateOrElevateKt1Caller()
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Stub-User", MainAdmin.ToString());
        Assert.That((await client.GetAsync($"/api/kt1/courses/{DemoStore.Course}/administration")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await client.PostAsync("/api/kt1/session/ana", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync($"/api/kt1/attempts/{DemoStore.MarkoAttempt}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Kt1AdminCookieDoesNotGrantMainAccountRegistrationPermission()
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await client.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PostAsJsonAsync("/api/admin/accounts", new
        {
            email = "integration@example.invalid",
            password = "Synthetic!Password42"
        })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task EnablingKt1PreservesLaboratoryRoutesAndTheirOwnAccessChecks()
    {
        using var factory = new Kt1Factory();
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await admin.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // A KT1 administrative cookie cannot authenticate the separate laboratory module.
        Assert.That((await admin.GetAsync("/api/laboratories/")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        using var laboratoryStudent = factory.CreateClient();
        laboratoryStudent.DefaultRequestHeaders.Add("X-Test-User", "ana:Student");
        Assert.That((await laboratoryStudent.GetAsync("/api/laboratories/")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await laboratoryStudent.PostAsync("/api/laboratories/", null)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        // The laboratory's Development header cannot grant KT1 access either.
        Assert.That((await laboratoryStudent.GetAsync($"/api/kt1/courses/{DemoStore.Course}/administration")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public void EnablingKt1StillRejectsInvalidMainRateLimitConfiguration()
    {
        using var baseline = new Kt1Factory();
        using var configured = baseline.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:Login:PermitLimit", "0"));
        var error = Assert.Catch(() => configured.CreateClient());
        Assert.That(error?.GetBaseException(), Is.InstanceOf<OptionsValidationException>());
    }
}
