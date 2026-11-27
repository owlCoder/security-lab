using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Kt1;

namespace Platform.Kt1.Tests;

public sealed class Kt1Factory(string environment = "Development", bool enabled = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(environment).UseSetting("Kt1Demo:Enabled", enabled.ToString());
}
[TestFixture]
public class Kt1HttpTests
{
    private Kt1Factory factory = null!;
    private HttpClient client = null!;
    private DemoStore Store => factory.Services.GetRequiredService<DemoStore>();
    private static string Attempt(Guid id) => $"/api/kt1/attempts/{id}";
    private static string Course(Guid id) => $"/api/kt1/courses/{id}";
    [SetUp] public void Setup() { factory = new(); client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Kt1-Demo", "1"); }
    [TearDown] public void Cleanup() { client.Dispose(); factory.Dispose(); }
    private async Task Login(string persona) => Assert.That((await client.PostAsync($"/api/kt1/session/{persona}", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    private async Task<GrantView> Grant(params string[] caps)
    {
        await Login("admin");
        var result = await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.DelegateUser, caps));
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await result.Content.ReadFromJsonAsync<GrantView>())!;
    }
    [TestCase("Development", false)] [TestCase("Production", true)]
    public async Task SyntheticRoutesRequireDevelopmentAndExplicitFlag(string environment, bool enabled)
    {
        using var isolated = new Kt1Factory(environment, enabled);
        using var c = isolated.CreateClient();
        Assert.That((await c.GetAsync("/api/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await c.GetAsync("/api/kt1/info")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await c.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task SharedTeamDefaultDenyDemoIsPreserved()
    {
        var body = await client.GetStringAsync("/api/access/demo-deny");
        Assert.That(body, Does.Contain("denialReason"));
        Assert.That(body, Does.Not.Contain("Synthetic Ana answer"));
    }
    [TestCase("ana", 200)] [TestCase("marko", 404)] [TestCase("teacher", 200)]
    [TestCase("foreign-teacher", 404)] [TestCase("blocked", 404)] [TestCase("assistant", 404)]
    [TestCase("admin", 404)] [TestCase("other-admin", 404)] [TestCase("delegate", 404)]
    public async Task ConcreteReadChecksOwnershipMembershipAndTeachingRole(string persona, int expected)
    {
        await Login(persona);
        Assert.That((int)(await client.GetAsync(Attempt(DemoStore.AnaAttempt))).StatusCode, Is.EqualTo(expected));
    }
    [Test] public async Task AnonymousReadAndWriteRequireSession()
    {
        Assert.That((await client.GetAsync(Attempt(DemoStore.DraftAttempt))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest("x", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task ForeignAndMissingAreIdenticalEvenWithMembershipOutage(bool offline)
    {
        await Login("ana"); Store.MembershipsUnavailable = offline;
        var foreign = await client.GetAsync(Attempt(DemoStore.MarkoAttempt));
        var missing = await client.GetAsync(Attempt(DemoStore.MissingAttempt));
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await foreign.Content.ReadAsStringAsync(), Is.EqualTo(await missing.Content.ReadAsStringAsync()));
        Assert.That(foreign.Headers.CacheControl?.NoStore, Is.True);
    }
    [TestCase("identity")] [TestCase("attempts")] [TestCase("membership")]
    public async Task DependencyOutageDeniesOwnerReadAndWriteWithoutMutation(string dependency)
    {
        await Login("ana");
        Store.AccountsUnavailable = dependency == "identity";
        Store.AttemptsUnavailable = dependency == "attempts";
        Store.MembershipsUnavailable = dependency == "membership";
        Assert.That((await client.GetAsync(Attempt(DemoStore.DraftAttempt))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest("secret", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Store.AccountsUnavailable = Store.AttemptsUnavailable = Store.MembershipsUnavailable = false;
        Assert.That(Store.FindAttempt(DemoStore.DraftAttempt).Value!.Version, Is.EqualTo(1));
    }
    [Test] public async Task DraftUpdatePersistsAndStaleVersionFailsWithMetadataOnlyAudit()
    {
        await Login("ana");
        const string answer = "Do not put this answer into the audit.";
        var response = await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest(answer, 1));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var value = await client.GetFromJsonAsync<AttemptView>(Attempt(DemoStore.DraftAttempt));
        Assert.That(value!.Answer, Is.EqualTo(answer)); Assert.That(value.Version, Is.EqualTo(2));
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest("lost update", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
        var entry = Store.Events.Single(e => e.Reason == "OWNER_DRAFT_UPDATED");
        Assert.That(entry.VersionBefore, Is.EqualTo(1)); Assert.That(entry.VersionAfter, Is.EqualTo(2));
        Assert.That(entry.CorrelationId, Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
        Assert.That(JsonSerializer.Serialize(Store.Events), Does.Not.Contain(answer));
    }
    [TestCase("marko")] [TestCase("teacher")] [TestCase("admin")] [TestCase("assistant")] [TestCase("blocked")]
    public async Task NonOwnerCannotWriteAndLearnsNoVersionConflict(string persona)
    {
        await Login(persona);
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest("x", 999))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(Store.FindAttempt(DemoStore.DraftAttempt).Value!.Version, Is.EqualTo(1));
    }
    [TestCase(1)] [TestCase(5)]
    public async Task SubmittedAndCompletedAttemptsAreImmutable(int number)
    {
        await Login("ana");
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.Id(3, number)) + "/answer", new UpdateDraftRequest("x", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }
    [TestCase(null, 1)] [TestCase("", 1)] [TestCase(" ", 1)] [TestCase("answer", 0)]
    public async Task InvalidDraftInputIsRejected(string? answer, int version)
    {
        await Login("ana");
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest(answer, version))).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test] public async Task UnsubmittedAnswerStaysPrivateFromTeacher()
    { await Login("teacher"); Assert.That((await client.GetAsync(Attempt(DemoStore.DraftAttempt))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound)); }
    [Test] public async Task MissingMutationHeaderCannotChangeSessionOrDraft()
    {
        await Login("ana"); client.DefaultRequestHeaders.Remove("X-Kt1-Demo");
        Assert.That((await client.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.PutAsJsonAsync(Attempt(DemoStore.DraftAttempt) + "/answer", new UpdateDraftRequest("x", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test] public async Task ForgedActorHeadersDoNotElevateStudent()
    {
        await Login("marko"); client.DefaultRequestHeaders.Add("X-Account-Id", DemoStore.Ana.ToString());
        client.DefaultRequestHeaders.Add("X-Role", "Admin");
        Assert.That((await client.GetAsync(Attempt(DemoStore.AnaAttempt))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [TestCase("teacher")] [TestCase("ana")] [TestCase("other-admin")] [TestCase("delegate")]
    public async Task CourseAdministrationRequiresExplicitGrantInThisCourse(string persona)
    {
        await Login(persona);
        Assert.That((await client.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations",
            new DelegateRequest(DemoStore.Marko, ["ViewAdministration"]))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task DelegationRenameRevokeIsScopedAndImmediatelyEffective()
    {
        // The original client stays the administrator; the delegate keeps a separate existing session.
        var grant = await Grant("ViewAdministration", "ManageSettings");
        Assert.That(Store.Events.Single(e => e.Action == AdministrationPolicy.Delegate && e.Outcome == "ALLOW").TargetId, Is.EqualTo(grant.Id));
        using var delegateClient = factory.CreateClient();
        delegateClient.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await delegateClient.PostAsync("/api/kt1/session/delegate", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var initial = await delegateClient.GetFromJsonAsync<AdministrationView>(Course(DemoStore.Course) + "/administration");
        Assert.That(initial!.Capabilities, Does.Not.Contain("ManageDelegations"));
        Assert.That((await delegateClient.PutAsJsonAsync(Course(DemoStore.Course) + "/settings", new RenameCourseRequest("Delegated title", initial.Version))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await delegateClient.GetAsync(Course(DemoStore.OtherCourse) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await delegateClient.GetAsync(Attempt(DemoStore.AnaAttempt))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await delegateClient.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.Marko, ["ViewAdministration"]))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + $"/delegations/{grant.Id}/revoke", new RevokeRequest(grant.Version))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        // No sign-in call after revocation: these are requests from the previously authenticated client.
        Assert.That((await delegateClient.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await delegateClient.PutAsJsonAsync(Course(DemoStore.Course) + "/settings", new RenameCourseRequest("after revoke", 2))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(Store.FindCourse(DemoStore.Course).Value!.DisplayName, Is.EqualTo("Delegated title"));
        Assert.That(Store.Events.Count(e => e.Action == AdministrationPolicy.Revoke && e.Outcome == "ALLOW"), Is.EqualTo(1));
    }
    [Test] public async Task ViewOnlyDelegateCannotRename()
    {
        await Grant("ViewAdministration"); await Login("delegate");
        Assert.That((await client.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PutAsJsonAsync(Course(DemoStore.Course) + "/settings", new RenameCourseRequest("forbidden", 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }
    [TestCase("ManageDelegations")] [TestCase("Teacher")] [TestCase("*")] [TestCase("ManageSettings")]
    public async Task InvalidOrEscalatingCapabilitySetIsRejected(string capability)
    {
        await Login("admin");
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.DelegateUser, [capability]))).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test] public async Task RootIsCourseScopedAndCannotRevokeOtherCourseOrRootGrant()
    {
        var grant = await Grant("ViewAdministration");
        await Login("other-admin");
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.OtherCourse) + $"/delegations/{grant.Id}/revoke", new RevokeRequest(1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        await Login("admin");
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + $"/delegations/{DemoStore.RootGrant}/revoke", new RevokeRequest(1))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task BlockedDelegateLosesAuthorityEvenWithExistingCookie()
    {
        await Grant("ViewAdministration"); await Login("delegate"); Store.SetAccountActive(DemoStore.DelegateUser, false);
        Assert.That((await client.GetAsync(Course(DemoStore.Course) + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task AdminOutageIsFailClosedAndDoesNotRevealMissingCourse()
    {
        await Login("admin"); Store.AdministrationUnavailable = true;
        var a = await client.GetAsync(Course(DemoStore.Course) + "/administration");
        var b = await client.GetAsync(Course(DemoStore.MissingCourse) + "/administration");
        Assert.That(a.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await a.Content.ReadAsStringAsync(), Is.EqualTo(await b.Content.ReadAsStringAsync()));
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.DelegateUser, ["ViewAdministration"]))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task SelfDelegationAndInactiveTargetAreRejected()
    {
        await Login("admin");
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.Admin, ["ViewAdministration"]))).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.Blocked, ["ViewAdministration"]))).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test] public async Task DuplicateDelegationAndStaleRevokeFailWithoutLosingGrant()
    {
        var grant = await Grant("ViewAdministration");
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + "/delegations", new DelegateRequest(DemoStore.DelegateUser, ["ViewAdministration"]))).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await client.PostAsJsonAsync(Course(DemoStore.Course) + $"/delegations/{grant.Id}/revoke", new RevokeRequest(999))).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(Store.FindAuthority(DemoStore.DelegateUser, DemoStore.Course).Value!.State, Is.EqualTo(GrantState.Active));
    }
    [TestCase("")] [TestCase("\ninvalid")]
    public async Task CourseNameValidationAndStaleVersionPreventMutation(string name)
    {
        await Login("admin");
        Assert.That((await client.PutAsJsonAsync(Course(DemoStore.Course) + "/settings", new RenameCourseRequest(name, 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.PutAsJsonAsync(Course(DemoStore.Course) + "/settings", new RenameCourseRequest("good", 999))).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(Store.FindCourse(DemoStore.Course).Value!.Version, Is.EqualTo(1));
    }
}
