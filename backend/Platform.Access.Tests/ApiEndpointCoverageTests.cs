using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Access.AspNet;

namespace Platform.Access.Tests.AspNet;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class ApiEndpointCoverageTests
{
    [Test]
    public void ACC05_Every_real_route_has_exactly_one_access_requirement()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Development"));
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();

        Assert.That(endpoints, Is.Not.Empty);

        var violations = new List<string>();
        foreach (var e in endpoints)
        {
            var route = "/" + e.RoutePattern.RawText?.TrimStart('/');
            var requirements = e.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();
            if (requirements.Count != 1)
                violations.Add($"{route}: expected exactly one access requirement");
        }

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task ACC05_Health_is_public_and_protected_lab_route_is_denied_without_membership()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        Assert.That((await client.GetAsync("/api/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var res = await client.GetAsync($"/api/laboratories/{Guid.NewGuid()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await res.Content.ReadAsStringAsync(), Does.Not.Contain("laboratory"));

        var competencies = await client.GetAsync("/api/competencies");
        Assert.That(competencies.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        Assert.That((await client.GetAsync("/api/test/no-metadata")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
