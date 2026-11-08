using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using static Platform.Audit.Tests.Reading.ReadingExamples;

namespace Platform.Audit.Tests.Reading;

/// <summary>
/// AUD-06 through the real API host: real Access middleware, real course audit policy, real
/// session-confirmed subject provider and real controller. Only the data sources are fakes
/// (sessions, accounts, memberships, audit store/writer), so no database is needed.
/// </summary>
[TestFixture]
[Property("RequirementId", "AUD-06")]
public sealed class AuditEventsHttpTests
{
    private static readonly Guid Course = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Course2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Teacher = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AssistantNoGrant = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid AssistantWithGrant = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
    private static readonly Guid TeacherOfOtherCourse = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");

    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private Factory _factory = null!;
    private HttpClient _client = null!;
    private FakeQueryStore _store = null!;
    private FakeTrustedWriter _writer = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FakeQueryStore();
        _store.Rows.Add(Event(Course, T0));
        _store.Rows.Add(Event(Course2, T0.AddMinutes(1)));
        _writer = new FakeTrustedWriter();
        _factory = new Factory(_store, _writer);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Teacher_ReadsOwnCourse_ViewIsRecordedWithSessionActorAndServerCorrelation()
    {
        using var response = await Get(Course, "teacher");

        var viewed = _writer.Written.Single(entry => entry.Draft.EventType == AuditEventTypes.AuditEventsViewed);
        var correlationHeader = response.Headers.GetValues("X-Correlation-ID").Single();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(json.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(_store.LastCourseId, Is.EqualTo(Course));
            Assert.That(viewed.Context.Actor, Is.EqualTo(AuditActor.ForUser(Teacher)));
            Assert.That(viewed.Context.CorrelationId, Is.EqualTo(correlationHeader));
            Assert.That(viewed.Draft.CourseId, Is.EqualTo(Course));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        }
    }

    [Test]
    public async Task AssistantWithDelegation_Reads_ButAssistantWithoutDelegation_IsDenied()
    {
        using var allowed = await Get(Course, "assistant-grant");
        var callsAfterAllowed = _store.Calls;
        using var denied = await Get(Course, "assistant-nogrant");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(_store.Calls, Is.EqualTo(callsAfterAllowed), "a denied request never reaches the store");
        }
    }

    [Test]
    public async Task Student_IsDenied_AndSeesNoEvents()
    {
        using var response = await Get(Course, "student");
        var body = await response.Content.ReadAsStringAsync();

        AssertDeniedWithoutLeak(response, body, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TeacherOfAnotherCourse_IsDenied_IdorByRouteCourse()
    {
        using var response = await Get(Course, "teacher-other");
        var body = await response.Content.ReadAsStringAsync();

        AssertDeniedWithoutLeak(response, body, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Request_WithoutSession_Is401_AndWithAnUnknownToken_IsNotServed()
    {
        using var anonymous = await Get(Course, token: null);
        using var forged = await Get(Course, "forged-token");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(forged.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
            Assert.That(_store.Calls, Is.Zero);
        }
    }

    [Test]
    public async Task CourseInTheQueryString_CannotWidenTheAuthorizedRouteCourse()
    {
        using var response = await _client.SendAsync(Request($"/api/audit/courses/{Course}/events?courseId={Course2}", "teacher"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_store.LastCourseId, Is.EqualTo(Course));
        }
    }

    [Test]
    public async Task WhenTheViewCannotBeRecorded_TheResponseIs503_AndCarriesNoEvents()
    {
        _writer.Result = AuditWriteResult.Unavailable();

        using var response = await Get(Course, "teacher");
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body, Does.Not.Contain(TargetAccountMarker));
        }
    }

    [Test]
    public async Task InvalidFilters_Return400_AndRecordNothing()
    {
        using var badLimit = await Get(Course, "teacher", "?limit=100000");
        using var badType = await Get(Course, "teacher", "?eventType=made.up.type");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(badLimit.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(badType.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(_writer.Written.Where(entry => entry.Draft.EventType == AuditEventTypes.AuditEventsViewed), Is.Empty);
        }
    }

    [Test]
    public async Task ThereIsNoHttpRouteThatWritesAuditEvents()
    {
        using var post = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/audit/courses/{Course}/events") { Content = new StringContent("{}") }
                .WithSession("teacher"));

        Assert.That(post.StatusCode, Is.AnyOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound, HttpStatusCode.Forbidden));
        Assert.That(_writer.Written.Where(entry => entry.Draft.EventType != AuditEventTypes.AccessDenied), Is.Empty);
    }

    private static string TargetAccountMarker => AuditDraftExamples.TargetAccountId.ToString();

    private void AssertDeniedWithoutLeak(HttpResponseMessage response, string body, HttpStatusCode expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            Assert.That(body, Does.Not.Contain(TargetAccountMarker));
            Assert.That(body, Does.Not.Contain("role.changed"));
            Assert.That(_store.Calls, Is.Zero);
            Assert.That(_writer.Written.Where(entry => entry.Draft.EventType == AuditEventTypes.AuditEventsViewed), Is.Empty);
        }
    }

    private Task<HttpResponseMessage> Get(Guid course, string? token, string query = "") =>
        _client.SendAsync(Request($"/api/audit/courses/{course}/events{query}", token));

    private static HttpRequestMessage Request(string url, string? token) =>
        new HttpRequestMessage(HttpMethod.Get, url).WithSession(token);

    private sealed class Factory(FakeQueryStore store, FakeTrustedWriter writer) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                var memberships = new InMemoryMembershipReader();
                memberships.Set(Member(Teacher, Course, CourseRole.Teacher));
                memberships.Set(Member(Student, Course, CourseRole.Student));
                memberships.Set(Member(AssistantNoGrant, Course, CourseRole.Assistant));
                memberships.Set(Member(AssistantWithGrant, Course, CourseRole.Assistant, "audit.events.read"));
                memberships.Set(Member(TeacherOfOtherCourse, Course2, CourseRole.Teacher));

                services.AddSingleton<IMembershipReader>(memberships);
                services.AddScoped<ISessionManager>(_ => new FakeSessions());
                services.AddScoped<IAccountStatusReader>(_ => new FakeAccounts());
                services.AddScoped<IAuditEventQueryStore>(_ => store);
                services.AddScoped<ITrustedAuditWriter>(_ => writer);
            });
        }

        private static CourseMembership Member(Guid account, Guid course, CourseRole role, params string[] actions) =>
            new(account, course, role, MembershipStatus.Active, new HashSet<string>(actions, StringComparer.Ordinal));
    }

    private sealed class FakeSessions : ISessionManager
    {
        private static readonly Dictionary<string, Guid> Tokens = new()
        {
            ["teacher"] = Teacher,
            ["student"] = Student,
            ["assistant-nogrant"] = AssistantNoGrant,
            ["assistant-grant"] = AssistantWithGrant,
            ["teacher-other"] = TeacherOfOtherCourse
        };

        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(Tokens.TryGetValue(token, out var user)
                ? new ValidateSession(Guid.NewGuid(), user, AuthenticationLevel.Active)
                : null);

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionInfo>>([]);
    }

    private sealed class FakeAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(new AccountSnapshot(accountId, AccountStatus.Active));
    }
}

internal static class RequestExtensions
{
    public static HttpRequestMessage WithSession(this HttpRequestMessage request, string? token)
    {
        if (token is not null)
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        return request;
    }
}
