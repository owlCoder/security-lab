using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Assignments.Api;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Api.Modules.Assignments.Composition;
using Platform.Api.Modules.Assignments.Integration;
using Platform.Api.Modules.Assignments.Persistence;
using Platform.Api.Services.Identity;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Tests.AssignmentsAuditApi;
using AccessMembershipReader = Platform.Access.Contracts.IMembershipReader;

namespace Platform.Tests.AssignmentsIntegration;

// ASN-04 / ASN-06 TIM 15: ceo HTTP tok sa PRODUKCIONOM kompozicijom portova.
// Program.cs + AddAssignmentsAccessAuthorization() (pravi Access modul) + AddAssignmentsPostgres() (prava baza)
// + PostgresTransactionalAuditWriter (pravi Audit modul u transakciji). Zamenjeni su samo spoljni izvori
// koje ova celina ne poseduje: sesija i status naloga (Identity), članstvo na kursu (tim 3) i netransakcioni
// Audit writer (tim 12, da bi se zapisi mogli pročitati u testu).
[TestFixture]
[Category("AssignmentsPostgres")]
[NonParallelizable]
public sealed class AssignmentsProductionCompositionHttpTests
{
    private const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    private const string OtherCourse = "c0c0c0c0-0000-4000-8000-000000000016";
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private const string Student = "22222222-2222-2222-2222-222222222222";
    private const string Outsider = "33333333-3333-3333-3333-333333333333";
    private const string OtherTeacher = "66666666-6666-6666-6666-666666666666";
    private const string NotEnrolledStudent = "77777777-7777-7777-7777-777777777777";

    private AssignmentsTestDatabase _database = null!;
    private ProductionHostFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _database = await AssignmentsTestDatabase.CreateAsync();

        var memberships = new InMemoryMembershipReader();
        memberships.Set(Member(Teacher, Course, CourseRole.Teacher));
        memberships.Set(Member(Student, Course, CourseRole.Student));
        memberships.Set(Member(OtherTeacher, OtherCourse, CourseRole.Teacher));

        _factory = new ProductionHostFactory(_database.ConnectionString, memberships);
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [SetUp]
    public void SetUp() => _factory.Audit.Clear();

    // ---------- kompozicija ----------

    [Test]
    public void ProductionComposition_EveryPortIsARealAdapter()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.Multiple(() =>
        {
            Assert.That(AssignmentsCompositionCheck.GetNotConfiguredPorts(services), Is.Empty);
            Assert.That(services.GetRequiredService<IAssignmentRepository>(), Is.TypeOf<PostgresAssignmentRepository>());
            Assert.That(services.GetRequiredService<IAttemptStore>(), Is.TypeOf<PostgresAttemptStore>());
            Assert.That(services.GetRequiredService<IDeadlineExtensionStore>(), Is.TypeOf<PostgresDeadlineExtensionStore>());
            Assert.That(services.GetRequiredService<IStartAttemptAuthorization>(), Is.TypeOf<AccessDecisionStartAttemptAuthorization>());
            Assert.That(services.GetRequiredService<IDeadlineExtensionAuthorization>(), Is.TypeOf<AccessDecisionDeadlineExtensionAuthorization>());
            Assert.That(services.GetRequiredService<ICourseEnrollmentCheck>(), Is.TypeOf<MembershipCourseEnrollmentCheck>());
        });
    }

    // ---------- ASN-04 ----------

    [Test]
    public async Task StartAttempt_EnrolledStudent_UsesRealLimit_AndIdempotencyKey()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var key = NewKey();

        using var first = await _client.SendAsync(StartAttempt(assignment.Id, Student, key));
        using var retry = await _client.SendAsync(StartAttempt(assignment.Id, Student, key));
        using var aboveLimit = await _client.SendAsync(StartAttempt(assignment.Id, Student, NewKey()));

        var firstId = (await Json(first)).GetProperty("attemptId").GetGuid();
        var retryId = (await Json(retry)).GetProperty("attemptId").GetGuid();
        var code = (await Json(aboveLimit)).GetProperty("code").GetString();
        var stored = await CountAttemptsAsync(assignment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(retryId, Is.EqualTo(firstId));
            Assert.That(aboveLimit.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo(AssignmentErrorCodes.AttemptLimitReached));
            Assert.That(stored, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartAttempt_ConcurrentRequests_RealDatabaseEnforcesTheLimit()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 2);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var response = await _client.SendAsync(StartAttempt(assignment.Id, Student, NewKey()));
            return response.StatusCode;
        }));

        var stored = await CountAttemptsAsync(assignment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Count(s => s == HttpStatusCode.Created), Is.EqualTo(2));
            Assert.That(statuses.Count(s => s == HttpStatusCode.Conflict), Is.EqualTo(10));
            Assert.That(stored, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StartAttempt_OutsiderAndUnknownAssignment_SameResponse_NothingStored()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 3);

        using var outsider = await _client.SendAsync(StartAttempt(assignment.Id, Outsider, NewKey()));
        using var teacher = await _client.SendAsync(StartAttempt(assignment.Id, Teacher, NewKey()));
        using var unknown = await _client.SendAsync(StartAttempt(Guid.NewGuid(), Student, NewKey()));

        var outsiderBody = await outsider.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var stored = await CountAttemptsAsync(assignment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(outsider.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(teacher.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(outsiderBody, Is.EqualTo(unknownBody));
            Assert.That(stored, Is.EqualTo(0));
        });
    }

    // ---------- ASN-06 ----------

    [Test]
    public async Task ExtendDeadline_CourseTeacher_Returns200_StoresDeadline_AndAuditIsNotLost()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var newDue = assignment.DueAtUtc.AddDays(3);

        using var response = await _client.SendAsync(ExtendDeadline(assignment.Id, Student, Teacher, newDue));

        var body = await Json(response);
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        var overrides = await _database.CountAsync(
            "SELECT count(*) FROM assignments.deadline_overrides WHERE assignment_id = @a AND extended_by_id = @t;",
            ("a", assignment.Id), ("t", Teacher));
        var inAudit = await _database.CountAsync(
            "SELECT count(*) FROM audit.audit_events WHERE correlation_id = @c;", ("c", correlationId));
        var inOutbox = await _database.CountAsync(
            "SELECT count(*) FROM assignments.audit_outbox WHERE correlation_id = @c AND actor_id = @t;",
            ("c", correlationId), ("t", Guid.Parse(Teacher)));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("dueAtUtc").GetDateTimeOffset(), Is.EqualTo(newDue));
            Assert.That(overrides, Is.EqualTo(1));
            // Tačno jedan trag uspeha: u audit šemi (kada tim 12 doda tip) ili u outbox-u, u istoj transakciji.
            Assert.That(inAudit + inOutbox, Is.EqualTo(1));
        });
    }

    [TestCase(Student, TestName = "ExtendDeadline_Student_Returns403_NothingStored")]
    [TestCase(OtherTeacher, TestName = "ExtendDeadline_TeacherOfAnotherCourse_Returns403_NothingStored")]
    [TestCase(Outsider, TestName = "ExtendDeadline_Outsider_Returns403_NothingStored")]
    public async Task ExtendDeadline_NotTheCourseTeacher_Returns403(string caller)
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);

        using var response = await _client.SendAsync(
            ExtendDeadline(assignment.Id, Student, caller, assignment.DueAtUtc.AddDays(3)));

        var overrides = await _database.CountAsync(
            "SELECT count(*) FROM assignments.deadline_overrides WHERE assignment_id = @a;", ("a", assignment.Id));
        var denied = _factory.Audit.Calls.Where(call => call.Draft.Outcome == AuditOutcome.Denied).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(overrides, Is.EqualTo(0));
            // Odbijanje je zabeleženo (dok tim 12 ne doda assignment.* kao access.denied), akter iz sesije.
            Assert.That(denied, Is.Not.Empty);
            Assert.That(denied.Select(call => call.Context.Actor).Distinct(),
                Is.EqualTo(new[] { AuditActor.ForUser(Guid.Parse(caller)) }));
        });
    }

    [Test]
    public async Task ExtendDeadline_StudentNotEnrolled_Returns409_NothingStored()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);

        using var response = await _client.SendAsync(
            ExtendDeadline(assignment.Id, NotEnrolledStudent, Teacher, assignment.DueAtUtc.AddDays(3)));

        var code = (await Json(response)).GetProperty("code").GetString();
        var overrides = await _database.CountAsync(
            "SELECT count(*) FROM assignments.deadline_overrides WHERE assignment_id = @a;", ("a", assignment.Id));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo(AssignmentErrorCodes.DeadlineExtensionRejected));
            Assert.That(overrides, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExtendDeadline_SecondExtension_UsesStoredIndividualDeadline()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var first = assignment.DueAtUtc.AddDays(2);

        using var ok = await _client.SendAsync(ExtendDeadline(assignment.Id, Student, Teacher, first));
        using var shorter = await _client.SendAsync(ExtendDeadline(assignment.Id, Student, Teacher, assignment.DueAtUtc.AddDays(1)));
        using var later = await _client.SendAsync(ExtendDeadline(assignment.Id, Student, Teacher, assignment.DueAtUtc.AddDays(4)));

        var laterBody = await Json(later);

        Assert.Multiple(() =>
        {
            Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // Novi rok mora biti posle TRENUTNOG individualnog roka iz baze, ne samo posle roka dodele.
            Assert.That(shorter.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(laterBody.GetProperty("previousDueAtUtc").GetDateTimeOffset(), Is.EqualTo(first));
        });
    }

    // ---------- pomoćne metode ----------

    private async Task<Assignment> AddAssignmentAsync(int maxAttempts)
    {
        var assignment = new Assignment(
            Guid.NewGuid(), Course, "revision-1", null,
            new DateTimeOffset(DateTime.UtcNow.AddDays(7).Date, TimeSpan.Zero), maxAttempts);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAssignmentRepository>().AddAsync(assignment);
        return assignment;
    }

    private Task<long> CountAttemptsAsync(Guid assignmentId) =>
        _database.CountAsync("SELECT count(*) FROM assignments.attempts WHERE assignment_id = @a;", ("a", assignmentId));

    private static CourseMembership Member(string account, string course, CourseRole role) =>
        new(Guid.Parse(account), Guid.Parse(course), role, MembershipStatus.Active, new HashSet<string>(StringComparer.Ordinal));

    private static HttpRequestMessage StartAttempt(Guid assignmentId, string user, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assignments/{assignmentId:D}/attempts");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static HttpRequestMessage ExtendDeadline(Guid assignmentId, string studentId, string user, DateTimeOffset newDue)
    {
        var json = JsonSerializer.Serialize(new { newDueAtUtc = newDue.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") });
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/assignments/{assignmentId:D}/students/{studentId}/deadline-extensions")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        return request;
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class ProductionHostFactory(string connectionString, AccessMembershipReader memberships)
        : WebApplicationFactory<Program>
    {
        private static readonly HashSet<string> KnownUsers =
            [Teacher, Student, Outsider, OtherTeacher, NotEnrolledStudent];

        public RecordingTrustedAuditWriter Audit { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager, TestSessionManager>();
                services.RemoveAll<IAccountStatusReader>();
                services.AddSingleton<IAccountStatusReader, TestAccounts>();

                services.RemoveAll<ITrustedAuditWriter>();
                services.AddSingleton<ITrustedAuditWriter>(Audit);

                // Izvor članstva tima 3; odluku i dalje donosi pravi Access modul.
                services.RemoveAll<AccessMembershipReader>();
                services.AddSingleton(memberships);

                // Isti poziv kao Program.cs kada postoji ConnectionStrings:Assignments.
                services.AddAssignmentsPostgres(connectionString);
            });
        }

        private sealed class TestSessionManager : ISessionManager
        {
            public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
                Task.FromResult(KnownUsers.Contains(token)
                    ? new ValidateSession(Guid.NewGuid(), Guid.Parse(token), AuthenticationLevel.Active)
                    : null);

            public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private sealed class TestAccounts : IAccountStatusReader
        {
            public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
                Task.FromResult<AccountSnapshot?>(KnownUsers.Contains(accountId.ToString("D"))
                    ? new AccountSnapshot(accountId, AccountStatus.Active)
                    : null);
        }
    }
}
