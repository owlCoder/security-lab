using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Kt1;

namespace Platform.Kt1.Tests;

[TestFixture]
public class AnonymousAuditTests
{
    private Kt1Factory baseline = null!;
    private WebApplicationFactory<Program> host = null!;
    private HttpClient client = null!;
    private DemoStore Store => host.Services.GetRequiredService<DemoStore>();
    private RecordingAccessAudit Central => host.Services.GetRequiredService<RecordingAccessAudit>();

    [SetUp]
    public void Setup()
    {
        baseline = new Kt1Factory();
        host = baseline.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Observe the real central decision audit; do not replace its decision engine.
            services.RemoveAll<IAccessAuditWriter>();
            services.AddSingleton<RecordingAccessAudit>();
            services.AddSingleton<IAccessAuditWriter>(sp => sp.GetRequiredService<RecordingAccessAudit>());
        }));
        client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
    }

    [TearDown]
    public void Cleanup() { client.Dispose(); host.Dispose(); baseline.Dispose(); }

    public static IEnumerable<TestCaseData> ResourceRequests()
    {
        var attempt = $"/api/kt1/attempts/{DemoStore.DraftAttempt}";
        var course = $"/api/kt1/courses/{DemoStore.Course}";
        yield return new TestCaseData("GET", attempt, AttemptPolicy.Read, "attempt", DemoStore.DraftAttempt);
        yield return new TestCaseData("PUT", attempt + "/answer", AttemptPolicy.Update, "attempt", DemoStore.DraftAttempt);
        yield return new TestCaseData("GET", course + "/administration", AdministrationPolicy.Read, "course", DemoStore.Course);
        yield return new TestCaseData("PUT", course + "/settings", AdministrationPolicy.Rename, "course", DemoStore.Course);
        yield return new TestCaseData("POST", course + "/delegations", AdministrationPolicy.Delegate, "course", DemoStore.Course);
        yield return new TestCaseData("POST", course + $"/delegations/{DemoStore.RootGrant}/revoke", AdministrationPolicy.Revoke, "course", DemoStore.Course);
    }

    [TestCaseSource(nameof(ResourceRequests))]
    public async Task AnonymousResourceRequestRecordsKt1AuditBeforeCentralDenialWithoutMutation(
        string method, string path, string action, string type, Guid target)
    {
        var draftBefore = Store.FindAttempt(DemoStore.DraftAttempt).Value;
        var courseBefore = Store.FindCourse(DemoStore.Course).Value;
        var rootBefore = Store.FindAuthority(DemoStore.Admin, DemoStore.Course).Value;
        object? body = action switch
        {
            AttemptPolicy.Update => new UpdateDraftRequest("anonymous-answer-must-not-be-logged", 1),
            AdministrationPolicy.Rename => new RenameCourseRequest("anonymous-title-must-not-be-logged", 1),
            AdministrationPolicy.Delegate => new DelegateRequest(DemoStore.DelegateUser, ["ViewAdministration"]),
            AdministrationPolicy.Revoke => new RevokeRequest(1),
            _ => null
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = JsonContent.Create(body);
        var before = DateTimeOffset.UtcNow;
        using var response = await client.SendAsync(request);
        var after = DateTimeOffset.UtcNow;
        AssertAnonymousAudits(response, Kt1AccessActions.Central(action), type, target);
        var entry = Store.Events.Single();
        Assert.That(entry.Timestamp, Is.InRange(before, after));
        Assert.That(entry.VersionBefore, Is.Null);
        Assert.That(entry.VersionAfter, Is.Null);
        Assert.That(JsonSerializer.Serialize(entry), Does.Not.Contain("must-not-be-logged"));
        Assert.That(Store.FindAttempt(DemoStore.DraftAttempt).Value, Is.EqualTo(draftBefore));
        Assert.That(Store.FindCourse(DemoStore.Course).Value, Is.EqualTo(courseBefore));
        Assert.That(Store.FindAuthority(DemoStore.Admin, DemoStore.Course).Value, Is.EqualTo(rootBefore));
        Assert.That(Store.FindAuthority(DemoStore.DelegateUser, DemoStore.Course).Value, Is.Null);
    }

    [Test]
    public async Task ForgedCookieAndActorHeadersStillProduceNullActorAudit()
    {
        client.DefaultRequestHeaders.Add("Cookie", "SecurityLab.CourseworkKt1=invalid-cookie-must-not-be-logged");
        client.DefaultRequestHeaders.Add("X-Account-Id", DemoStore.Admin.ToString());
        client.DefaultRequestHeaders.Add("X-Stub-User", DemoStore.Admin.ToString());
        client.DefaultRequestHeaders.Add("X-Role", "Admin");
        using var response = await client.GetAsync($"/api/kt1/attempts/{DemoStore.DraftAttempt}");
        AssertAnonymousAudits(response, Kt1AccessActions.Central(AttemptPolicy.Read), "attempt", DemoStore.DraftAttempt);
        Assert.That(JsonSerializer.Serialize(Store.Events), Does.Not.Contain("invalid-cookie-must-not-be-logged"));
    }

    [Test]
    public async Task MalformedAnonymousTargetIsAuditedWithoutStoringRawInput()
    {
        using var response = await client.GetAsync("/api/kt1/attempts/invalid-target-must-not-be-logged");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(Store.Events, Has.Count.EqualTo(1));
        var entry = Store.Events.Single();
        Assert.That(entry.ActorId, Is.Null);
        Assert.That(entry.Reason, Is.EqualTo("UNAUTHENTICATED"));
        Assert.That(entry.Outcome, Is.EqualTo("DENY"));
        Assert.That(entry.Action, Is.EqualTo(Kt1AccessActions.Central(AttemptPolicy.Read)));
        Assert.That(entry.TargetId, Is.EqualTo(Guid.Empty));
        Assert.That(entry.CourseId, Is.Null);
        Assert.That(entry.CorrelationId, Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
        Assert.That(JsonSerializer.Serialize(entry), Does.Not.Contain("invalid-target-must-not-be-logged"));
        var central = Central.Entries.Single();
        Assert.That(central.Request.Subject, Is.Null);
        Assert.That(central.Decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
        Assert.That(central.Kt1EventsAtWrite, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task PublicKt1SessionAndInfoRoutesDoNotCreateAnonymousDenialEvents()
    {
        Assert.That((await client.GetAsync("/api/kt1/info")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PostAsync("/api/kt1/session/ana", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PostAsync("/api/kt1/logout", null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(Store.Events, Is.Empty);
        Assert.That(Central.Entries, Has.Count.EqualTo(3));
        Assert.That(Central.Entries.All(e => e.Decision.Outcome == AccessOutcome.Allow), Is.True);
    }

    [Test]
    public async Task NonKt1EndpointsKeepTheirOwnAuditBoundary()
    {
        Assert.That((await client.GetAsync("/api/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/laboratories/")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(Store.Events, Is.Empty);
        Assert.That(Central.Entries.Any(e => e.Decision.Outcome == AccessOutcome.Deny), Is.True);
    }

    private void AssertAnonymousAudits(HttpResponseMessage response, string action, string type, Guid target)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(Store.Events, Has.Count.EqualTo(1));
        var entry = Store.Events.Single();
        Assert.That(entry.ActorId, Is.Null);
        Assert.That(entry.Action, Is.EqualTo(action));
        Assert.That(entry.TargetId, Is.EqualTo(target));
        Assert.That(entry.CourseId, Is.EqualTo(type == "course" ? (Guid?)target : null));
        Assert.That(entry.Outcome, Is.EqualTo("DENY"));
        Assert.That(entry.Reason, Is.EqualTo("UNAUTHENTICATED"));
        Assert.That(entry.PolicyVersion, Is.EqualTo("v0"));
        Assert.That(entry.CorrelationId, Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
        Assert.That(Central.Entries, Has.Count.EqualTo(1));
        var central = Central.Entries.Single();
        Assert.That(central.Request.Subject, Is.Null);
        Assert.That(central.Request.Action, Is.EqualTo(action));
        Assert.That(central.Request.Resource, Is.EqualTo(new ResourceRef(type, target.ToString("D"))));
        Assert.That(central.Decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(central.Decision.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
        Assert.That(central.Request.CorrelationId, Is.EqualTo(entry.CorrelationId));
        Assert.That(central.Kt1EventsAtWrite, Has.Length.EqualTo(1));
        Assert.That(central.Kt1EventsAtWrite.Single(), Is.EqualTo(entry));
    }

    private sealed class RecordingAccessAudit(DemoStore store) : IAccessAuditWriter
    {
        public ConcurrentQueue<(AccessRequest Request, AccessDecision Decision, SecurityEvent[] Kt1EventsAtWrite)> Entries { get; } = new();
        public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
        {
            Entries.Enqueue((request, decision, store.Events.ToArray()));
            return ValueTask.CompletedTask;
        }
    }
}
