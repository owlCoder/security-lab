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
    private static readonly HashSet<string> ApprovedPublicRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/health",
        "/api/access/demo-deny",   
    };

    // Each of these existing routes has an independently tested authorization
    // guard or credential check in its own handler/filter. No new route may be
    // delegated without an explicit review and addition to this inventory.
    private static readonly HashSet<string> ApprovedDelegatedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/admin/accounts",
        "/api/admin/accounts/{id:guid}/status",
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/authoring/templates",
        "/api/authoring/templates/{templateId:guid}/drafts",
        "/api/laboratories/",
        "/api/laboratories/search",
        "/api/laboratories/{id:guid}",
        "/api/laboratories/{id:guid}/title",
        "/api/labs/",
        "/api/labs/search",
        "/api/labs/{id:guid}",
        "/api/labs/{id:guid}/title",
    };

    [Test]
    public void ACC05_Every_real_route_is_protected_or_approved_public()
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
            var hasReq = e.Metadata.GetMetadata<IAccessRequirementMetadata>() is not null;
            var isPublic = e.Metadata.GetMetadata<PublicEndpointMetadata>() is not null
                           || e.Metadata.GetMetadata<AllowPublicAttribute>() is not null;
            var isDelegated = e.Metadata.GetMetadata<DelegatedAuthorizationAttribute>() is not null;

            if ((hasReq ? 1 : 0) + (isPublic ? 1 : 0) + (isDelegated ? 1 : 0) > 1)
                violations.Add($"{route}: conflicting access metadata");
            else if (!hasReq && !isPublic && !isDelegated)
                violations.Add($"{route}: no access metadata");
            else if (isPublic && !ApprovedPublicRoutes.Contains(route))
                violations.Add($"{route}: public but not approved");
            else if (isDelegated && !ApprovedDelegatedRoutes.Contains(route))
                violations.Add($"{route}: legacy delegation not explicitly reviewed");
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

        Assert.That((await client.GetAsync("/api/test/no-metadata")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
