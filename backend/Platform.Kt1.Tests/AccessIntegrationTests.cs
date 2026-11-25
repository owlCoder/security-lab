using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Platform.Access.Application.Services;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Kt1;

namespace Platform.Kt1.Tests;

[TestFixture]
public class AccessIntegrationTests
{
    [Test]
    public void EveryKt1RouteHasExactlyOneRegisteredActionAndNoLegacyMarker()
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        var catalog = factory.Services.GetRequiredService<IActionCatalog>();
        var policies = factory.Services.GetRequiredService<IAccessPolicyRegistry>();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<Kt1EndpointMetadata>() is not null).ToArray();
        Assert.That(routes, Has.Length.EqualTo(9));
        foreach (var endpoint in routes)
        {
            var requirements = endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();
            Assert.That(requirements, Has.Count.EqualTo(1), endpoint.DisplayName);
            var action = requirements[0].Action;
            Assert.That(action, Does.StartWith("kt1."));
            Assert.That(catalog.TryGet(action, out var definition), Is.True);
            Assert.That(policies.TryGet(action, out _), Is.True);
            Assert.That(definition!.ResourceType, Is.EqualTo(requirements[0].ResourceType));
            Assert.That(definition.Scope, Is.EqualTo(requirements[0].ResourceType is null ? ActionScope.Public : ActionScope.Course));
#pragma warning disable CS0618
            Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
#pragma warning restore CS0618
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GlobalCentralDenialOrFailureStopsHttpAndDirectServiceDraftWrites(bool throwDecision)
    {
        var calls = new ConcurrentQueue<AccessRequest>();
        using var baseline = new Kt1Factory();
        using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessDecision>();
            services.AddSingleton<IAccessDecision>(sp => new ObservedDecision(
                ActivatorUtilities.CreateInstance<DefaultDenyAccessDecision>(sp), calls, throwDecision));
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await client.PostAsync("/api/kt1/session/ana", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var path = $"/api/kt1/attempts/{DemoStore.DraftAttempt}/answer";
        Assert.That((await client.PutAsJsonAsync(path, new UpdateDraftRequest("must not persist", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AttemptService>();
        if (throwDecision)
            Assert.ThrowsAsync<InvalidOperationException>(async () => await service.Execute(DemoStore.Ana,
                DemoStore.DraftAttempt, new("also denied", 1), "direct-service"));
        else
            Assert.That((await service.Execute(DemoStore.Ana, DemoStore.DraftAttempt,
                new("also denied", 1), "direct-service")).Status, Is.EqualTo(404));
        var writes = calls.Where(c => c.Action == Kt1AccessActions.Central(AttemptPolicy.Update)).ToArray();
        Assert.That(writes, Has.Length.EqualTo(2));
        Assert.That(writes.All(c => c.Subject?.Id == DemoStore.Ana.ToString("D") &&
            c.Resource == new ResourceRef("attempt", DemoStore.DraftAttempt.ToString("D"))), Is.True);
        Assert.That(factory.Services.GetRequiredService<DemoStore>().FindAttempt(DemoStore.DraftAttempt).Value!.Version,
            Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ForgedHeadersAndInvalidCookieCannotAuthenticateKt1(bool write)
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        client.DefaultRequestHeaders.Add("X-Test-User", $"{DemoStore.Ana}:Administrator");
        client.DefaultRequestHeaders.Add("X-Stub-User", DemoStore.Ana.ToString());
        client.DefaultRequestHeaders.Add("X-Account-Id", DemoStore.Ana.ToString());
        client.DefaultRequestHeaders.Add("Cookie", "SecurityLab.CourseworkKt1=forged");
        var path = $"/api/kt1/attempts/{DemoStore.DraftAttempt}";
        var response = write ? await client.PutAsJsonAsync(path + "/answer", new UpdateDraftRequest("denied", 1))
            : await client.GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Services.GetRequiredService<DemoStore>().FindAttempt(DemoStore.DraftAttempt).Value!.Version,
            Is.EqualTo(1));
    }

    [Test]
    public async Task LogoutRemovesKt1IdentityAndUnknownSyntheticActionsStayDenied()
    {
        using var factory = new Kt1Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await client.PostAsync("/api/kt1/session/ana", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync($"/api/kt1/attempts/{DemoStore.AnaAttempt}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PostAsync("/api/kt1/logout", null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.GetAsync($"/api/kt1/attempts/{DemoStore.AnaAttempt}")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var decision = await factory.Services.GetRequiredService<IAccessDecision>().DecideAsync(new(
            new(DemoStore.Admin.ToString(), AccountState.Active), "kt1.attempt.delete",
            new("attempt", DemoStore.AnaAttempt.ToString()), "unknown-kt1"));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public void Kt1RegistrationMustPrecedeCentralCatalogFreeze()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();
        Assert.Throws<InvalidOperationException>(() => services.AddCourseworkKt1());
    }

    private sealed class ObservedDecision(IAccessDecision inner, ConcurrentQueue<AccessRequest> calls, bool throwDecision) : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            calls.Enqueue(request);
            if (request.Action != Kt1AccessActions.Central(AttemptPolicy.Update))
                return inner.DecideAsync(request, cancellationToken);
            if (throwDecision) throw new InvalidOperationException("Synthetic central decision failure");
            return ValueTask.FromResult(AccessDecision.Deny(AccessDenialReason.PolicyDenied, request.CorrelationId));
        }
    }
}
