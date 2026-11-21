using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Integration;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Administration.Api;
using Platform.Api.Modules.Administration.Integration;
using Platform.Api.Modules.Kt1;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using SharedMembership = Platform.Access.Domain.Models.CourseMembership;
using SharedRole = Platform.Access.Domain.Enums.CourseRole;
using MembershipStatus = Platform.Access.Domain.Enums.MembershipStatus;

namespace Platform.Coursework.Tests;

public sealed partial class AdministrationHttpTests
{
    private RootProvisioner Provisioner() => new(connection, factory.Services.GetRequiredService<ITrustedCourseDirectory>(),
        factory.Services.GetRequiredService<IdentityAdministrationAccounts>());
    private async Task RemoveFirstRoot()
        => await Sql("DELETE FROM coursework_admin.grants WHERE course_id=@id; DELETE FROM coursework_admin.course_settings WHERE course_id=@id",("id",DemoStore.Course));

    [Test]
    public async Task OperatorBootstrapUsesActualCatalogAndIdentityAndIsIdempotent()
    {
        await RemoveFirstRoot();
        var first = await Provisioner().BootstrapAsync(DemoStore.Course,DemoStore.Admin);
        var again = await Provisioner().BootstrapAsync(DemoStore.Course,DemoStore.Admin);
        Assert.That(first.Created,Is.True);
        Assert.That(again.Created,Is.False);
        Assert.That(again.GrantId,Is.EqualTo(first.GrantId));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE action='course.root.bootstrap'"),Is.EqualTo(1L));
        Assert.That(await Sql("SELECT administrative_label FROM coursework_admin.course_settings WHERE course_id=@id",("id",DemoStore.Course)),Is.EqualTo("Catalog name A"));
        var (root,_) = await Login(DemoStore.Admin);
        Assert.That((await root.GetAsync(Path())).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await root.PutAsJsonAsync(Path()+"/settings",new RenameCourseRequest("Local administrative label",1))).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        await using var catalog=CoursesDb();
        Assert.That((await catalog.Courses.SingleAsync(c=>c.Id==DemoStore.Course)).Name,Is.EqualTo("Catalog name A"));
    }

    [TestCase("missing-course"),TestCase("archived-course"),TestCase("missing-account"),TestCase("blocked-account")]
    public async Task BootstrapRejectsUntrustedOrInactiveProviderRecords(string mode)
    {
        await RemoveFirstRoot();
        if(mode=="archived-course") { await using var c=CoursesDb(); var row=await c.Courses.SingleAsync(c=>c.Id==DemoStore.Course); row.Archive(DemoStore.Admin); await c.SaveChangesAsync(); }
        if(mode=="blocked-account") { await using var db=IdentityDb(); await db.Accounts.Where(a=>a.Id==DemoStore.Admin).ExecuteUpdateAsync(s=>s.SetProperty(a=>a.Status,AccountStatus.Blocked)); }
        Assert.ThrowsAsync<InvalidOperationException>(async()=>await Provisioner().BootstrapAsync(
            mode=="missing-course"?Guid.NewGuid():DemoStore.Course,mode=="missing-account"?Guid.NewGuid():DemoStore.Admin));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.course_settings WHERE course_id=@id",("id",DemoStore.Course)),Is.Zero);
    }

    [Test]
    public void BootstrapCannotReplaceAnExistingRoot()
        => Assert.ThrowsAsync<InvalidOperationException>(async()=>await Provisioner().BootstrapAsync(DemoStore.Course,DemoStore.DelegateUser));

    [Test]
    public async Task BootstrapAuditFailureRollsBackBothRootAndSettings()
    {
        await RemoveFirstRoot();
        await Sql("""
            CREATE FUNCTION coursework_admin.reject_audit() RETURNS trigger LANGUAGE plpgsql AS
              $$ BEGIN RAISE EXCEPTION 'injected producer audit failure'; END $$;
            CREATE TRIGGER reject_audit BEFORE INSERT ON coursework_admin.outbox FOR EACH ROW EXECUTE FUNCTION coursework_admin.reject_audit();
            """);
        Assert.CatchAsync<Exception>(async()=>await Provisioner().BootstrapAsync(DemoStore.Course,DemoStore.Admin));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.course_settings WHERE course_id=@id",("id",DemoStore.Course)),Is.Zero);
    }

    [Test]
    public async Task ConcurrentBootstrapCreatesOneRootAndOneEvent()
    {
        await RemoveFirstRoot();
        async Task<bool> Start(Guid account) { try { await Provisioner().BootstrapAsync(DemoStore.Course,account); return true; } catch(InvalidOperationException) { return false; } }
        var results=await Task.WhenAll(Start(DemoStore.Admin),Start(DemoStore.DelegateUser));
        Assert.That(results.Count(x=>x),Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants WHERE course_id=@id AND is_root",("id",DemoStore.Course)),Is.EqualTo(1L));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE action='course.root.bootstrap'"),Is.EqualTo(1L));
    }

    [TestCase("archived"),TestCase("deleted"),TestCase("outage")]
    public async Task CatalogChangesDenyExistingCookieWithoutRevealingCourseExistence(string mode)
    {
        var(root,cookie)=await Login(DemoStore.Admin);
        Assert.That((await root.GetAsync(Path())).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        await using(var c=CoursesDb())
        {
            if(mode=="outage") await c.Database.ExecuteSqlRawAsync("ALTER TABLE courses.\"Courses\" RENAME TO unavailable_courses");
            else { var row=await c.Courses.SingleAsync(c=>c.Id==DemoStore.Course); if(mode=="deleted")row.IsDeleted=true; else row.Archive(DemoStore.Admin); await c.SaveChangesAsync(); }
        }
        var replay=Client(cookie);
        var present=await replay.GetAsync(Path()); var missing=await replay.GetAsync(Path(Guid.NewGuid()));
        Assert.That(present.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await missing.Content.ReadAsStringAsync(),Is.EqualTo(await present.Content.ReadAsStringAsync()));
        Assert.That((await replay.PutAsJsonAsync(Path()+"/settings",new RenameCourseRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(false, TestName = "ArchivedCourseAtPreflightCommitsDenialWithoutChangingSettings")]
    [TestCase(true, TestName = "UnavailableCatalogAtPreflightCommitsDenialWithoutChangingSettings")]
    public async Task CatalogFailureAtPreflightCommitsDenialWithoutChangingSettings(bool catalogUnavailable)
    {
        var (root, _) = await Login(DemoStore.Admin);
        var allowed = 0;
        factory.AfterAllow = async request =>
        {
            // Let the real middleware decision pass, then change the catalog before Execute.
            if (request.Action != AdministrationPolicy.Rename || ++allowed != 1) return;
            if (catalogUnavailable)
            {
                await Sql("ALTER TABLE courses.\"Courses\" RENAME TO unavailable_courses");
                return;
            }
            await using var catalog = CoursesDb();
            var course = await catalog.Courses.SingleAsync(c => c.Id == DemoStore.Course);
            course.Archive(DemoStore.Admin);
            await catalog.SaveChangesAsync();
        };

        using var response = await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("must not be stored", 1));
        Assert.That(allowed, Is.EqualTo(1));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("""{"code":"RESOURCE_UNAVAILABLE"}"""));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox"), Is.EqualTo(1L));
        Assert.That(await Sql("""
            SELECT count(*) FROM coursework_admin.outbox
            WHERE correlation_id = @correlation AND actor_id = @actor AND action = @action
              AND target_id = @course AND course_id = @course AND outcome = 'DENY'
              AND reason = 'COURSE_UNAVAILABLE' AND policy_version = 'adm08:v1'
              AND version_before IS NULL AND version_after IS NULL
            """, ("correlation", correlation), ("actor", DemoStore.Admin), ("action", AdministrationPolicy.Rename),
            ("course", DemoStore.Course)), Is.EqualTo(1L));
        Assert.That(await Sql("""
            SELECT count(*) FROM coursework_admin.course_settings
            WHERE course_id = @course AND administrative_label = 'Administrative label A' AND version = 1
            """, ("course", DemoStore.Course)), Is.EqualTo(1L));
    }

    [Test]
    public async Task CourseArchivedDuringMutationRollsBackBeforeCommit()
    {
        var(root,_)=await Login(DemoStore.Admin); int n=0;
        factory.AfterAllow=async r=> { if(r.Action!=AdministrationPolicy.Rename || ++n!=2)return;
            await using var c=CoursesDb(); var row=await c.Courses.SingleAsync(c=>c.Id==DemoStore.Course); row.Archive(DemoStore.Admin); await c.SaveChangesAsync(); };
        Assert.That((await root.PutAsJsonAsync(Path()+"/settings",new RenameCourseRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id=@id",("id",DemoStore.Course)),Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome='ALLOW'"),Is.Zero);
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome='DENY' AND reason='COURSE_UNAVAILABLE'"),Is.EqualTo(1L));
    }

    private async Task<AuditRelayResult> Relay()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        return await new AdministrationAuditRelay(connection,scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()).RunOnceAsync();
    }
    [Test]
    public async Task RealCentralAuditAcknowledgesDelegationAndRevocationAndDeduplicatesCrashRetry()
    {
        var(root,_)=await Login(DemoStore.Admin); var grant=await Delegate(root);
        await root.PostAsJsonAsync(Path()+$"/delegations/{grant.Id}/revoke",new RevokeRequest(1));
        var first=await Relay(); Assert.That(first.Acknowledged,Is.EqualTo(2)); Assert.That(first.Failed,Is.Zero);
        Assert.That((await Relay()).Acknowledged,Is.Zero);
        Assert.That(await Sql("SELECT count(*) FROM audit.audit_events WHERE event_type LIKE 'course.delegation.%'"),Is.EqualTo(4L));
        Assert.That(await Sql("SELECT count(*) FROM audit.audit_events WHERE event_type LIKE 'course.delegation.%' AND actor_id=@actor",("actor",DemoStore.Admin)),Is.EqualTo(4L));
        await Sql("DELETE FROM coursework_admin.outbox_deliveries"); // simulate lost local receipts after successful central append
        Assert.That((await Relay()).Acknowledged,Is.EqualTo(2));
        Assert.That(await Sql("SELECT count(*) FROM audit.audit_events WHERE event_type LIKE 'course.delegation.%'"),Is.EqualTo(4L));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox"),Is.EqualTo(2L));
    }
    [Test]
    public async Task AuditOutageLeavesDeliveryUnacknowledgedAndRetrySucceeds()
    {
        var(root,_)=await Login(DemoStore.Admin); await Delegate(root);
        await Sql("ALTER TABLE audit.audit_events RENAME TO unavailable_events");
        var failed=await Relay(); Assert.That(failed.Acknowledged,Is.Zero); Assert.That(failed.Failed,Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox_deliveries"),Is.Zero);
        await Sql("ALTER TABLE audit.unavailable_events RENAME TO audit_events");
        Assert.That((await Relay()).Acknowledged,Is.EqualTo(1));
    }
    [Test]
    public async Task UnmappedSettingsEventsAreRetainedWithoutInventingCentralTypes()
    {
        var(root,_)=await Login(DemoStore.Admin);
        await root.PutAsJsonAsync(Path()+"/settings",new RenameCourseRequest("local label",1));
        var relay=await Relay(); Assert.That(relay.Acknowledged,Is.Zero); Assert.That(relay.RetainedWithoutCatalogMapping,Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox_deliveries"),Is.Zero);
    }

    private async Task EnableAttemptBoundary(bool membership=true)
    { await factory.DisposeAsync(); factory=new(connection,attempts:true,membershipProvider:membership); }
    private static string AttemptPath(Guid? id=null)=>$"/api/resource-attempts/{id??DemoStore.DraftAttempt}";

    [Test]
    public async Task Acc04ApiHasExplicitRequirementsAndDefaultDisabledRoutesRemainAbsent()
    {
        await EnableAttemptBoundary();
        var endpoints=factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s=>s.Endpoints)
            .Where(e=>e.Metadata.GetMetadata<Platform.Api.Modules.Access.Api.AttemptEndpointMetadata>() is not null).ToArray();
        Assert.That(endpoints.Length,Is.EqualTo(3));
        Assert.That(endpoints.All(e=>e.Metadata.GetOrderedMetadata<Platform.Access.AspNet.IAccessRequirementMetadata>().Count==1),Is.True);
        Assert.That(endpoints.All(e=>e.Metadata.GetMetadata<Platform.Access.AspNet.AllowPublicAttribute>() is null &&
            e.Metadata.GetMetadata<Platform.Access.AspNet.DelegatedAuthorizationAttribute>() is null),Is.True);
        await factory.DisposeAsync(); factory=new(connection);
        Assert.That((await Client().GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Acc04OtherCourseTeacherAndAdministrativeRootCannotReadStudentAnswer()
    {
        await EnableAttemptBoundary();
        foreach(var id in new[]{DemoStore.ForeignTeacher,DemoStore.Admin})
        {
            var(client,_)=await Login(id);
            Assert.That((await client.GetAsync(AttemptPath(DemoStore.AnaAttempt)+"/review")).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await client.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task Acc04RevokedSessionDuringDecisionDoesNotDispatchAProviderWrite()
    {
        await EnableAttemptBoundary(); var(owner,_)=await Login(DemoStore.Ana); int n=0;
        factory.AfterAllow=async request=>
        {
            if(request.Action!=AttemptPolicy.Update || ++n!=2)return;
            await using var db=IdentityDb();
            await db.Sessions.Where(s=>s.UserId==DemoStore.Ana).ExecuteUpdateAsync(s=>s.SetProperty(v=>v.RevokedAt,DateTimeOffset.UtcNow));
        };
        Assert.That((await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [Test]
    public async Task Acc04ConcurrentSameVersionWritesHaveOneWinner()
    {
        await EnableAttemptBoundary(); var(owner,cookie)=await Login(DemoStore.Ana); var second=Client(cookie);
        var responses=await Task.WhenAll(owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("one",1)),
            second.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("two",1)));
        Assert.That(responses.Count(r=>r.StatusCode==HttpStatusCode.OK),Is.EqualTo(1));
        Assert.That(responses.Count(r=>r.StatusCode==HttpStatusCode.Conflict),Is.EqualTo(1));
        Assert.That(factory.Attempts.Writes,Is.EqualTo(1)); Assert.That(factory.Attempts.Receipts.Count,Is.EqualTo(1));
    }

    [Test]
    public async Task Acc04ValidSyntheticCookieDoesNotAuthenticateTheRealSessionApi()
    {
        await factory.DisposeAsync(); factory=new(connection,environment:"Development",kt1:true,attempts:true);
        var synthetic=Client(); synthetic.DefaultRequestHeaders.Add("X-Kt1-Demo","1");
        var login=await synthetic.PostAsync("/api/kt1/session/ana",null);
        Assert.That(login.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        var cookie=login.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("SecurityLab.CourseworkKt1=",StringComparison.Ordinal)).Split(';')[0];
        Assert.That((await Client(cookie).GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Acc04RealSessionUsesSharedMembershipAndAttContractForOwnerReadAndCasWrite()
    {
        await EnableAttemptBoundary(); var(owner,_)=await Login(DemoStore.Ana);
        Assert.That((await owner.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        var update=await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("stored by ATT contract double",1));
        Assert.That(update.StatusCode,Is.EqualTo(HttpStatusCode.OK),await update.Content.ReadAsStringAsync());
        var read=await owner.GetFromJsonAsync<AttemptView>(AttemptPath());
        Assert.That(read!.Answer,Is.EqualTo("stored by ATT contract double")); Assert.That(read.Version,Is.EqualTo(2));
        Assert.That((await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("stale",1))).StatusCode,Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(factory.Attempts.Writes,Is.EqualTo(1)); Assert.That(factory.Attempts.Receipts.Count,Is.EqualTo(1));
        TestContext.Out.WriteLine("ACC04_CONTRACT_FLOW: real Identity login -> shared membership contract double -> ATT HTTP contract double -> snapshot-bound read -> CAS write -> stale 409. Actual ATT/ACC-02 providers are not on main.");
    }
    [TestCase(false),TestCase(true)]
    public async Task Acc04AnonymousAndForgedHeadersCannotReadOrWrite(bool forged)
    {
        await EnableAttemptBoundary(); var client=Client();
        if(forged)client.DefaultRequestHeaders.Add("X-Test-User",DemoStore.Ana.ToString());
        Assert.That((await client.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [TestCase(false),TestCase(true)]
    public async Task Acc04ForeignAndMissingStayIdenticalIncludingAttOutage(bool outage)
    {
        await EnableAttemptBoundary(); var(other,_)=await Login(DemoStore.Marko); factory.Attempts.Offline=outage;
        var foreign=await other.GetAsync(AttemptPath()); var missing=await other.GetAsync(AttemptPath(Guid.NewGuid()));
        Assert.That(foreign.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await missing.Content.ReadAsStringAsync(),Is.EqualTo(await foreign.Content.ReadAsStringAsync()));
        Assert.That((await other.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [Test]
    public async Task Acc04MissingMembershipProviderFailsClosedInsteadOfUsingDemoMembership()
    {
        await EnableAttemptBoundary(false); var(owner,_)=await Login(DemoStore.Ana);
        Assert.That((await owner.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [TestCase(false),TestCase(true)]
    public async Task Acc04MembershipRevocationOrOutageTakesEffectWithoutRelogin(bool outage)
    {
        await EnableAttemptBoundary(); var(owner,cookie)=await Login(DemoStore.Ana);
        Assert.That((await owner.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        factory.Members.Offline=outage; factory.Members.Left=true;
        var replay=Client(cookie);
        Assert.That((await replay.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await replay.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task Acc04CourseTeacherCanReviewSubmittedButCannotReadOrWritePrivateDraft()
    {
        await EnableAttemptBoundary(); var(teacher,_)=await Login(DemoStore.Teacher);
        Assert.That((await teacher.GetAsync(AttemptPath(DemoStore.AnaAttempt)+"/review")).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await teacher.GetAsync(AttemptPath()+"/review")).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await teacher.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [TestCase(false),TestCase(true)]
    public async Task Acc04SubmittedAndCompletedAnswersRemainImmutable(bool completed)
    {
        await EnableAttemptBoundary(); var(owner,_)=await Login(DemoStore.Ana);
        Assert.That((await owner.PutAsJsonAsync(AttemptPath(completed?DemoStore.CompletedAttempt:DemoStore.AnaAttempt)+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
    [Test]
    public async Task Acc04OwnerChangedAfterCentralDecisionNeverReceivesTheAnswer()
    {
        await EnableAttemptBoundary(); var(owner,_)=await Login(DemoStore.Ana);
        factory.AfterAllow=r=> { if(r.Action==AttemptPolicy.Read)factory.Attempts.ChangeOwner(DemoStore.Marko); return Task.CompletedTask; };
        Assert.That((await owner.GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Attempts.Reads,Is.Zero);
    }
    [Test]
    public async Task Acc04LogoutRejectsSameCookieAndInvalidInputNeverMutates()
    {
        await EnableAttemptBoundary(); var(owner,cookie)=await Login(DemoStore.Ana);
        Assert.That((await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("",1))).StatusCode,Is.EqualTo(HttpStatusCode.BadRequest));
        owner.DefaultRequestHeaders.Remove("X-Coursework-Request");
        Assert.That((await owner.PutAsJsonAsync(AttemptPath()+"/answer",new UpdateDraftRequest("no",1))).StatusCode,Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await owner.PostAsync("/api/auth/logout",null)).StatusCode,Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Client(cookie).GetAsync(AttemptPath())).StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Attempts.Writes,Is.Zero);
    }
}

// Explicit neighboring-provider test doubles. These are never registered by production composition.
internal sealed class MembershipProviderDouble : Platform.Access.Contracts.IMembershipReader
{
    public bool Left { get; set; } public bool Offline { get; set; }
    public ValueTask<SharedMembership?> GetAsync(Guid account,Guid course,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested(); if(Offline)throw new IOException("test provider unavailable");
        if(course!=DemoStore.Course || account==DemoStore.Admin || account==DemoStore.DelegateUser || account==DemoStore.ForeignTeacher)
            return ValueTask.FromResult<SharedMembership?>(null);
        return ValueTask.FromResult<SharedMembership?>(new(account,course,account==DemoStore.Teacher?SharedRole.Teacher:SharedRole.Student,
            Left?MembershipStatus.Left:MembershipStatus.Active,new HashSet<string>()));
    }
}
internal sealed class AttemptProviderHandler(AttemptProviderDouble provider) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>provider.SendAsync(request,ct);
}
internal sealed class AttemptProviderDouble
{
    public const string TestToken="fictional-service-token-for-isolated-tests-only";
    public bool Offline { get; set; } public int Writes { get; private set; } public int Reads { get; private set; }
    public List<Guid> Receipts { get; }=[];
    private readonly object gate=new();
    private readonly Dictionary<Guid,(AttemptSnapshot Snapshot,string Answer)> data=new()
    {
        [DemoStore.DraftAttempt]=(new(DemoStore.DraftAttempt,DemoStore.Ana,DemoStore.Course,AttemptState.InProgress,1),"private draft"),
        [DemoStore.AnaAttempt]=(new(DemoStore.AnaAttempt,DemoStore.Ana,DemoStore.Course,AttemptState.Submitted,1),"submitted answer"),
        [DemoStore.CompletedAttempt]=(new(DemoStore.CompletedAttempt,DemoStore.Ana,DemoStore.Course,AttemptState.Completed,1),"completed answer")
    };
    public void ChangeOwner(Guid owner) { lock(gate) { var d=data[DemoStore.DraftAttempt]; data[DemoStore.DraftAttempt]=(d.Snapshot with{OwnerId=owner,Version=d.Snapshot.Version+1},d.Answer); } }
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(Offline)return new(HttpStatusCode.ServiceUnavailable);
        Assert.That(request.Headers.Contains("Cookie"),Is.False);
        Assert.That(request.Headers.Authorization?.Parameter,Is.EqualTo(TestToken));
        Assert.That(request.Headers.GetValues(HttpAttemptStore.ContractHeader).Single(),Is.EqualTo(HttpAttemptStore.Contract));
        var parts=request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        if(parts.Length!=4 || parts[0]!="v1" || parts[1]!="attempts" || !Guid.TryParse(parts[2],out var id))return new(HttpStatusCode.NotFound);
        var read=parts[3]=="authorized-read"?await request.Content!.ReadFromJsonAsync<AuthorizedAnswerRead>(ct):null;
        var write=parts[3]=="authorized-draft"?await request.Content!.ReadFromJsonAsync<AuthorizedDraftWrite>(ct):null;
        object value;
        lock(gate)
        {
            if(!data.TryGetValue(id,out var d))return new(HttpStatusCode.NotFound);
            if(parts[3]=="metadata")value=d.Snapshot;
            else if(read is not null && read.Snapshot==d.Snapshot){Reads++;value=new AttemptView(id,d.Answer,d.Snapshot.Version);}
            else if(write is not null && write.Snapshot==d.Snapshot && write.ExpectedVersion==d.Snapshot.Version &&
                write.ActorId==d.Snapshot.OwnerId && d.Snapshot.State==AttemptState.InProgress)
            {
                var after=d.Snapshot with{Version=d.Snapshot.Version+1}; data[id]=(after,write.Answer); Writes++;
                var receipt=Guid.NewGuid(); Receipts.Add(receipt); value=new DraftWriteReceipt(new(id,write.Answer,after.Version),receipt);
            }
            else return new(write is not null?HttpStatusCode.Conflict:HttpStatusCode.NotFound);
        }
        var response=new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(value)};
        response.Headers.Add(HttpAttemptStore.ContractHeader,HttpAttemptStore.Contract); return response;
    }
}
