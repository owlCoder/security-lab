using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.AspNet;
using Platform.Api.Security.RevisionAuthorization;

namespace Platform.Tests.RevisionAuthorization;

public sealed class RevisionEndpointMetadataTests
{
    [Test]
    public void RevisionRoutes_RequireCentralAccess_WithoutLegacyDelegation()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = host.CreateClient();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/revisions", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(endpoints, Has.Length.EqualTo(7));
        foreach (var endpoint in endpoints)
        {
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action,
                    Is.EqualTo(RevisionActions.Entry), endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null, endpoint.DisplayName);
            });
        }
    }

    [Test]
    public void Login_IsExplicitlyPublic_WithoutLegacyDelegation()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = host.CreateClient();
        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Single(value => value.RoutePattern.RawText == "api/auth/login");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Not.Null);
            Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Not.Null);
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>(), Is.Null);
        });
    }
}
