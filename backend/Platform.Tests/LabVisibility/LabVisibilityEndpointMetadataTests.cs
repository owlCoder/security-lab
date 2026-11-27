using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Api.Security.IdentityAccess;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Policies;

namespace Platform.Tests.LabVisibility;

// #185: obe LabVisibility rute pod strogim ACC-05 middleware-om (isti obrazac kao REV, #192).
public sealed class LabVisibilityEndpointMetadataTests
{
    [TestCase("api/labs/{id:guid}/student-view")]
    [TestCase("api/labs/{id:guid}/preview")]
    public void LabRoute_HasExactlyOneEntryRequirement_WithoutMigrationMarkers(string route)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = host.CreateClient();

        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText == route);

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>(), Has.Count.EqualTo(1));
            Assert.That(endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action,
                Is.EqualTo(LabVisibilityActions.Entry));
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null);
        });
    }

    [Test]
    public void EntryAction_IsSessionBound_AndLabActionsUseRealPolicies()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = host.CreateClient();

        var catalog = host.Services.GetRequiredService<IActionCatalog>();
        var policies = host.Services.GetRequiredService<IAccessPolicyRegistry>();

        Assert.Multiple(() =>
        {
            // Subjekat ulazne radnje dolazi iz prave sesije, ne iz razvojnog zaglavlja.
            Assert.That(IdentityAccessActions.SessionBound, Does.Contain(LabVisibilityActions.Entry));

            Assert.That(catalog.TryGet(LabVisibilityActions.Entry, out var entry), Is.True);
            Assert.That(entry!.Scope, Is.EqualTo(ActionScope.Authenticated));
            Assert.That(entry.ResourceType, Is.Null);
            Assert.That(policies.TryGet(LabVisibilityActions.Entry, out var entryPolicy), Is.True);
            Assert.That(entryPolicy, Is.InstanceOf<LabVisibilityEntryPolicy>());

            // Stvarna odluka nad laboratorijom: kurs + resurs "lab" + prava politika (ne explicit-deny).
            Assert.That(catalog.TryGet(LabVisibilityActions.StudentView, out var studentView), Is.True);
            Assert.That(studentView!.Scope, Is.EqualTo(ActionScope.Course));
            Assert.That(studentView.ResourceType, Is.EqualTo("lab"));
            Assert.That(policies.TryGet(LabVisibilityActions.StudentView, out var studentPolicy), Is.True);
            Assert.That(studentPolicy, Is.InstanceOf<StudentViewPolicy>());

            Assert.That(catalog.TryGet(LabVisibilityActions.Preview, out var preview), Is.True);
            Assert.That(preview!.Scope, Is.EqualTo(ActionScope.Course));
            Assert.That(preview.ResourceType, Is.EqualTo("lab"));
            Assert.That(policies.TryGet(LabVisibilityActions.Preview, out var previewPolicy), Is.True);
            Assert.That(previewPolicy, Is.InstanceOf<LabPreviewPolicy>());
        });
    }
}
