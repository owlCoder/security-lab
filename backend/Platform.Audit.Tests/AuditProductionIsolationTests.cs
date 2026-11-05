using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
public sealed class AuditProductionIsolationTests
{
    [Test]
    public async Task Production_DoesNotExposeAuditProbeOrAccessDemo()
    {
        using var factory = new ProductionHostFactory();
        using var client = factory.CreateClient();
        using var probe = await client.PostAsJsonAsync(AuditHostFactory.ProbePath, StatusChange());
        using var demo = await client.GetAsync("/api/access/demo-deny");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(demo.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task Production_RejectsDevIdentityHeaderOnProtectedRoute()
    {
        using var factory = new ProductionHostFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/accounts/{TargetAccountId}/status")
        {
            Content = JsonContent.Create(new { status = "blocked", reason = "synthetic request" })
        };
        request.Headers.Add("X-Stub-User", UserId.ToString());
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private sealed class ProductionHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Production);
    }
}
