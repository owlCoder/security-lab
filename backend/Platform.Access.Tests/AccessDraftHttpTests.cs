using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Demo;

namespace Platform.Access.Tests;

public sealed class AccessDraftHttpTests
{
    private static string Attempt(Guid id) => $"/api/access-demo/attempts/{id}";
    private static async Task SignIn(HttpClient client, string persona = "ana")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/access-demo/session/{persona}");
        request.Headers.Add("X-Access-Demo", "1");
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
    private static async Task<HttpResponseMessage> Put(HttpClient client, Guid id, object body, bool header = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Attempt(id) + "/answer")
        { Content = JsonContent.Create(body) };
        if (header) request.Headers.Add("X-Access-Demo", "1");
        return await client.SendAsync(request);
    }

    [Test] public async Task OwnerUpdatesDraftAndReadsNewContent()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        using var write = await Put(client, DemoData.DraftAttempt, new { answer = "Izmenjen nacrt.", expectedVersion = 1 });
        var updated = await write.Content.ReadFromJsonAsync<AttemptView>();
        using var read = await client.GetAsync(Attempt(DemoData.DraftAttempt));
        var loaded = await read.Content.ReadFromJsonAsync<AttemptView>();
        Assert.Multiple(() =>
        {
            Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated?.Version, Is.EqualTo(2));
            Assert.That(updated?.Answer, Is.EqualTo("Izmenjen nacrt."));
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loaded, Is.EqualTo(updated));
        });
    }

    [TestCase("marko")] [TestCase("teacher")] [TestCase("foreign-teacher")]
    [TestCase("assistant")] [TestCase("admin")] [TestCase("blocked")]
    public async Task UnauthorizedDraftWriteDoesNotChangeState(string persona)
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        var data = app.Services.GetRequiredService<DemoData>();
        var original = data.FindAttempt(DemoData.DraftAttempt).Value!;
        await SignIn(client, persona);
        using var response = await Put(client, DemoData.DraftAttempt, new { answer = "Zabranjen upis.", expectedVersion = 1 });
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(data.FindAttempt(DemoData.DraftAttempt).Value, Is.EqualTo(original));
            Assert.That(data.ReadAnswer(original).Value?.Answer, Is.EqualTo("Nepredati testni odgovor."));
        });
    }

    [TestCase("30000000-0000-0000-0000-000000000001")]
    [TestCase("30000000-0000-0000-0000-000000000005")]
    public async Task SubmittedAndCompletedAttemptsStayImmutable(string id)
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        var data = app.Services.GetRequiredService<DemoData>();
        var original = data.FindAttempt(Guid.Parse(id)).Value!;
        var answer = data.ReadAnswer(original).Value;
        using var response = await Put(client, original.Id, new { answer = "Pokusaj prepisivanja istorije.", expectedVersion = 1 });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(data.ReadAnswer(original).Value, Is.EqualTo(answer));
    }

    [Test] public async Task ExpiredContextPreventsDraftMutation()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        using var response = await Put(client, DemoData.ClosedDraftAttempt, new { answer = "Kasni upis.", expectedVersion = 1 });
        var data = app.Services.GetRequiredService<DemoData>();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(data.FindAttempt(DemoData.ClosedDraftAttempt).Value?.Version, Is.EqualTo(1));
        Assert.That(data.Events.Single().ReasonCode, Is.EqualTo("CONTEXT_EXPIRED"));
    }

    [TestCase(null, 1)] [TestCase("", 1)] [TestCase("   ", 1)] [TestCase("Odgovor", 0)]
    public async Task InvalidDraftInputDoesNotChangeState(string? answer, int expectedVersion)
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        using var response = await Put(client, DemoData.DraftAttempt, new { answer, expectedVersion });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(app.Services.GetRequiredService<DemoData>().FindAttempt(DemoData.DraftAttempt).Value?.Version, Is.EqualTo(1));
    }

    [Test] public async Task OversizedAnswerIsRejected()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        using var response = await Put(client, DemoData.DraftAttempt,
            new { answer = new string('x', AttemptDraftService.MaxAnswerLength + 1), expectedVersion = 1 });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test] public async Task StaleVersionReturnsConflictWithoutLosingFirstWrite()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client);
        using var first = await Put(client, DemoData.DraftAttempt, new { answer = "Prva izmena.", expectedVersion = 1 });
        using var stale = await Put(client, DemoData.DraftAttempt, new { answer = "Zastarela izmena.", expectedVersion = 1 });
        var data = app.Services.GetRequiredService<DemoData>();
        var snapshot = data.FindAttempt(DemoData.DraftAttempt).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stale.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(snapshot.Version, Is.EqualTo(2));
            Assert.That(data.ReadAnswer(snapshot).Value?.Answer, Is.EqualTo("Prva izmena."));
            Assert.That(data.Events.Last().Outcome, Is.EqualTo("CONFLICT"));
        });
    }

    [Test] public async Task ForeignAndMissingDraftWritesHaveIdenticalPublicResponses()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        await SignIn(client, "marko");
        using var foreign = await Put(client, DemoData.DraftAttempt, new { answer = "Upis.", expectedVersion = 999 });
        using var missing = await Put(client, DemoData.MissingAttempt, new { answer = "Upis.", expectedVersion = 999 });
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
    }

    [TestCase("membership")] [TestCase("context")]
    public async Task DraftOutageDoesNotRevealExistence(string dependency)
    {
        using var app = new AccessFactory(configure: services =>
        {
            if (dependency == "membership") services.Replace(ServiceDescriptor.Singleton<IMembershipReader>(new OfflineMembership()));
            else services.Replace(ServiceDescriptor.Singleton<IContextPolicyReader>(new OfflineContext()));
        });
        using var client = app.CreateClient(); await SignIn(client, "marko");
        using var foreign = await Put(client, DemoData.DraftAttempt, new { answer = "Upis.", expectedVersion = 1 });
        using var missing = await Put(client, DemoData.MissingAttempt, new { answer = "Upis.", expectedVersion = 1 });
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        var events = app.Services.GetRequiredService<DemoData>().Events;
        Assert.That(events[0].ReasonCode, Is.EqualTo(dependency == "membership" ? "MEMBERSHIP_UNAVAILABLE" : "CONTEXT_UNAVAILABLE"));
        Assert.That(events[1].ReasonCode, Is.EqualTo("RESOURCE_NOT_FOUND"));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task UnavailableWriterFailsClosedWithoutMutation(bool timeout)
    {
        using var app = new AccessFactory(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IAttemptDraftWriter>(new OfflineWriter(timeout))));
        using var client = app.CreateClient(); await SignIn(client);
        using var response = await Put(client, DemoData.DraftAttempt, new { answer = "Upis.", expectedVersion = 1 });
        var data = app.Services.GetRequiredService<DemoData>();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(data.FindAttempt(DemoData.DraftAttempt).Value?.Version, Is.EqualTo(1));
        Assert.That(data.Events.Single().ReasonCode, Is.EqualTo(timeout ? "DEPENDENCY_TIMEOUT" : "ATTEMPT_STORE_UNAVAILABLE"));
    }

    [Test] public async Task AnonymousWriteIsRejected()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient();
        using var response = await Put(client, DemoData.DraftAttempt, new { answer = "Upis.", expectedVersion = 1 });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test] public async Task DraftWriteRequiresCustomHeader()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient(); await SignIn(client);
        using var response = await Put(client, DemoData.DraftAttempt, new { answer = "Upis.", expectedVersion = 1 }, header: false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(app.Services.GetRequiredService<DemoData>().FindAttempt(DemoData.DraftAttempt).Value?.Version, Is.EqualTo(1));
    }

    [Test] public async Task ExtraInputFieldsCannotChangeOwnershipCourseOrStatus()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient(); await SignIn(client);
        using var response = await Put(client, DemoData.DraftAttempt, new
        {
            answer = "Dozvoljen sadrzaj.", expectedVersion = 1,
            ownerId = DemoData.Marko, courseId = DemoData.OtherCourse, state = "Submitted", version = 1000, role = "Teacher"
        });
        var snapshot = app.Services.GetRequiredService<DemoData>().FindAttempt(DemoData.DraftAttempt).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(snapshot.OwnerId, Is.EqualTo(DemoData.Ana));
            Assert.That(snapshot.CourseId, Is.EqualTo(DemoData.Course));
            Assert.That(snapshot.State, Is.EqualTo(AttemptState.InProgress));
            Assert.That(snapshot.Version, Is.EqualTo(2));
        });
    }

    [Test] public async Task RevokedMembershipIsRecheckedBeforeWrite()
    {
        var membership = new SwitchableMembership();
        using var app = new AccessFactory(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IMembershipReader>(membership)));
        using var client = app.CreateClient(); await SignIn(client);
        using var before = await client.GetAsync(Attempt(DemoData.DraftAttempt));
        membership.Active = false;
        using var write = await Put(client, DemoData.DraftAttempt, new { answer = "Kasni upis.", expectedVersion = 1 });
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(app.Services.GetRequiredService<DemoData>().FindAttempt(DemoData.DraftAttempt).Value?.Version, Is.EqualTo(1));
    }

    [Test] public async Task ChangedContextIsRecheckedBeforeWrite()
    {
        var context = new SwitchableContext();
        using var app = new AccessFactory(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IContextPolicyReader>(context)));
        using var client = app.CreateClient(); await SignIn(client);
        using var before = await client.GetAsync(Attempt(DemoData.DraftAttempt));
        context.Closed = true;
        using var write = await Put(client, DemoData.DraftAttempt, new { answer = "Kasni upis.", expectedVersion = 1 });
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(app.Services.GetRequiredService<DemoData>().FindAttempt(DemoData.DraftAttempt).Value?.Version, Is.EqualTo(1));
    }

    [Test] public async Task UpdateAuditContainsMetadataWithoutAnswerOrSessionSecret()
    {
        using var app = new AccessFactory(); using var client = app.CreateClient(); await SignIn(client);
        const string answer = "OVAJ SADRZAJ NE SME U AUDIT";
        var started = DateTimeOffset.UtcNow;
        using var response = await Put(client, DemoData.DraftAttempt, new { answer, expectedVersion = 1 });
        var entry = app.Services.GetRequiredService<DemoData>().Events.Single();
        var json = JsonSerializer.Serialize(entry);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(entry.ActorId, Is.EqualTo(DemoData.Ana));
            Assert.That(entry.TargetId, Is.EqualTo(DemoData.DraftAttempt));
            Assert.That(entry.CourseId, Is.EqualTo(DemoData.Course));
            Assert.That(entry.Action, Is.EqualTo(AccessPolicy.UpdateDraftAnswer));
            Assert.That(entry.Outcome, Is.EqualTo("ALLOW"));
            Assert.That(entry.Timestamp, Is.InRange(started, DateTimeOffset.UtcNow));
            Assert.That(entry.Timestamp.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(entry.CorrelationId.ToString(), Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
            Assert.That(entry.ResourceVersionBefore, Is.EqualTo(1));
            Assert.That(entry.ResourceVersionAfter, Is.EqualTo(2));
            Assert.That(json, Does.Not.Contain(answer));
            Assert.That(json, Does.Not.Contain("SecurityLab.AccessKt1"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        });
        TestContext.Out.WriteLine("KT1_AUDIT_JSON:" + json);
    }

    private sealed class OfflineMembership : IMembershipReader
    {
        public PortResult<MembershipSnapshot> FindMembership(Guid accountId, Guid courseId) => PortResult<MembershipSnapshot>.Offline();
    }
    private sealed class OfflineContext : IContextPolicyReader
    {
        public PortResult<ContextPolicy> FindPolicy(string id) => PortResult<ContextPolicy>.Offline();
    }
    private sealed class OfflineWriter(bool timeout) : IAttemptDraftWriter
    {
        public DraftWriteResult TryUpdateDraft(AttemptSnapshot snapshot, int expectedVersion, string answer) =>
            timeout ? throw new TimeoutException() : new(DraftWriteStatus.Unavailable);
    }
    private sealed class SwitchableMembership : IMembershipReader
    {
        public bool Active { get; set; } = true;
        public PortResult<MembershipSnapshot> FindMembership(Guid accountId, Guid courseId) =>
            PortResult<MembershipSnapshot>.Found(new(accountId, courseId, CourseRole.Student, Active));
    }
    private sealed class SwitchableContext : IContextPolicyReader
    {
        public bool Closed { get; set; }
        public PortResult<ContextPolicy> FindPolicy(string id) => PortResult<ContextPolicy>.Found(
            new(id, "switchable:v1", null, Closed ? DateTimeOffset.UnixEpoch : null));
    }
}
