using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Api.Security.IdentityAccess;
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
                Assert.That(endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>(), Has.Count.EqualTo(1), endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null, endpoint.DisplayName);
            });
        }
    }

    [TestCase("api/auth/login", IdentityAccessActions.Login, ActionScope.Public)]
    [TestCase("api/auth/logout", IdentityAccessActions.Logout, ActionScope.Public)]
    [TestCase("api/auth/me", IdentityAccessActions.SessionRead, ActionScope.Authenticated)]
    public void AuthRoutes_RequireExactlyOneRegisteredPolicy_WithoutMigrationMarkers(
        string route, string action, ActionScope scope)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = host.CreateClient();
        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Single(value => value.RoutePattern.RawText == route);

        var catalog = host.Services.GetRequiredService<IActionCatalog>();
        var policies = host.Services.GetRequiredService<IAccessPolicyRegistry>();
        Assert.That(catalog.TryGet(action, out var definition), Is.True);
        Assert.That(policies.TryGet(action, out var policy), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(definition!.Scope, Is.EqualTo(scope));
            Assert.That(policy, Is.Not.Null);
            Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                Is.EqualTo(scope == ActionScope.Public));
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>(), Has.Count.EqualTo(1));
            Assert.That(endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action, Is.EqualTo(action));
        });
    }
}
