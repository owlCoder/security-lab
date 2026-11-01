using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Demo;

namespace Platform.Access.Tests;

public sealed class AccessFactory(string environment = "Development", bool enabled = true,
    Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("AccessDemo:Enabled", enabled.ToString());
        builder.ConfigureLogging(logging => logging.ClearProviders());
        if (configure is not null) builder.ConfigureServices(configure);
    }
}

public sealed class AccessHttpTests
{
    private static string Attempt(Guid id) => $"/api/access-demo/attempts/{id}";
    private static async Task SignIn(HttpClient client, string persona)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/access-demo/session/{persona}");
        request.Headers.Add("X-Access-Demo", "1");
        using var result = await client.SendAsync(request);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
    [Test] public async Task OwnerReceivesAnswerThroughHttp()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, "ana");
        using var response = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        var body = await response.Content.ReadFromJsonAsync<AttemptView>();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body?.Answer, Is.EqualTo("Sinteticki Anin odgovor."));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.Contains("X-Correlation-ID"), Is.True);
        });
    }
    [Test] public async Task ForeignAndMissingHaveIdenticalPublicResponses()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, "marko");
        using var foreign = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        using var missing = await client.GetAsync(Attempt(DemoData.MissingAttempt));
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        var audit = app.Services.GetRequiredService<DemoData>().Events;
        Assert.That(audit.Select(x => x.ReasonCode), Is.EqualTo(new[] { "NOT_OWNER_OR_COURSE_REVIEWER", "RESOURCE_NOT_FOUND" }));
    }
    [TestCase("teacher", 200)] [TestCase("foreign-teacher", 404)]
    [TestCase("blocked", 404)] [TestCase("assistant", 404)] [TestCase("admin", 404)]
    public async Task RoleAndAccountBoundariesAreEnforced(string persona, int status)
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, persona);
        using var response = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
    }
    [Test] public async Task OwnerCanBeDeniedByContext()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, "ana");
        using var result = await client.GetAsync(Attempt(DemoData.ClosedAttempt));
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(app.Services.GetRequiredService<DemoData>().Events.Single().ReasonCode, Is.EqualTo("CONTEXT_EXPIRED"));
    }
    [Test] public async Task ClientSuppliedActorRoleAndScopeDoNotChangeIdentity()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, "marko");
        client.DefaultRequestHeaders.Add("X-User-Id", DemoData.Ana.ToString());
        client.DefaultRequestHeaders.Add("X-Role", "Teacher");
        using var response = await client.GetAsync(Attempt(DemoData.AnaAttempt) + $"?actorId={DemoData.Ana}&role=Teacher&courseId={DemoData.Course}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task AnonymousCallerIsRejectedWithoutAnswer()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        using var response = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("odgovor"));
    }
    [Test] public async Task DemoSessionRequiresCustomHeader()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        using var result = await client.PostAsync("/api/access-demo/session/ana", null);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [TestCase("invalid")] [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task MalformedResourceIdIsRejected(string id)
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/access-demo/attempts/" + id);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [TestCase("Production", true)] [TestCase("Development", false)] [TestCase("Staging", true)]
    public async Task DemoRoutesAreAbsentOutsideExplicitDevelopmentMode(string environment, bool enabled)
    {
        using var app = new AccessFactory(environment, enabled); using var client = app.CreateClient();
        using var info = await client.GetAsync("/api/access-demo/info");
        using var login = await client.PostAsync("/api/access-demo/session/teacher", null);
        Assert.Multiple(() =>
        {
            Assert.That(info.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
    [TestCase("membership")] [TestCase("context")]
    public async Task DependencyOutageDoesNotRevealExistence(string dependency)
    {
        using var app = new AccessFactory(configure: services =>
        {
            if (dependency == "membership") services.Replace(ServiceDescriptor.Singleton<IMembershipReader>(new OfflineMembership()));
            else services.Replace(ServiceDescriptor.Singleton<IContextPolicyReader>(new OfflineContext()));
        });
        using var client = app.CreateClient(); await SignIn(client, "marko");
        using var foreign = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        using var missing = await client.GetAsync(Attempt(DemoData.MissingAttempt));
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        var events = app.Services.GetRequiredService<DemoData>().Events;
        Assert.That(events[0].ReasonCode, Is.EqualTo(dependency == "membership" ? "MEMBERSHIP_UNAVAILABLE" : "CONTEXT_UNAVAILABLE"));
        Assert.That(events[1].ReasonCode, Is.EqualTo("RESOURCE_NOT_FOUND"));
    }
    [Test] public async Task CurrentAccountStateIsRecheckedForEveryRequest()
    {
        var reader = new SwitchableAccount();
        using var app = new AccessFactory(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IAccountReader>(reader)));
        using var client = app.CreateClient(); await SignIn(client, "ana");
        using var before = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        reader.Active = false;
        using var after = await client.GetAsync(Attempt(DemoData.AnaAttempt));
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    private sealed class SwitchableAccount : IAccountReader
    {
        public bool Active { get; set; } = true;
        public PortResult<AccountSnapshot> FindAccount(Guid id) => PortResult<AccountSnapshot>.Found(new(id, Active));
    }
    private sealed class OfflineMembership : IMembershipReader
    {
        public PortResult<MembershipSnapshot> FindMembership(Guid accountId, Guid courseId) => PortResult<MembershipSnapshot>.Offline();
    }
    private sealed class OfflineContext : IContextPolicyReader
    {
        public PortResult<ContextPolicy> FindPolicy(string id) => PortResult<ContextPolicy>.Offline();
    }
}
