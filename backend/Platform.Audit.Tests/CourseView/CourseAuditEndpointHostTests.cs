using System.Net;
using System.Text.Json;
using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Contracts;
using Platform.Audit.Endpoints;
using Platform.Audit.Tests.Authorization;
using static Platform.Audit.Tests.Authorization.AuditAuthorizationExamples;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// S-14 through the real Platform.Api host: route, AccessControlMiddleware, session check, handler,
/// S-07 query service and audit.events.viewed. Test subject and memberships replace the parts other
/// teams still owe (see CourseAuditEndpointHostFactory). No database: the audit store is unreachable,
/// so every test here also proves that no data leaves the server when storage is down.
/// </summary>
[TestFixture]
public sealed class CourseAuditEndpointHostTests
{
    private static readonly Guid CourseM = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static string Teacher => TeacherId.ToString("D");

    private static async Task<HttpResponseMessage> Get(
        CourseAuditEndpointHostFactory factory, Guid course, string? subject, string query = "")
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, CourseAuditEndpointRoute.For(course) + query);
        if (subject is not null)
            request.Headers.Add(HeaderTestSubjectProvider.HeaderName, subject);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Errors(JsonElement problem) => problem.GetProperty("errors").EnumerateArray()
        .Select(error => error.GetProperty("field").GetString() + ":" + error.GetProperty("code").GetString())
        .ToArray();

    private static CourseAuditEndpointHostFactory WithTeacherOfK(
        ScriptedCourseAuditQueryService? query = null, ValidatingTrustedAuditWriter? writer = null)
    {
        var factory = new CourseAuditEndpointHostFactory { Query = query, Writer = writer };
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));
        return factory;
    }

    [Test]
    public async Task Anonymous_Gets401_WithoutEventsOrQuery()
    {
        var query = new ScriptedCourseAuditQueryService();
        using var factory = WithTeacherOfK(query);

        using var response = await Get(factory, CourseK, subject: null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(text, Does.Not.Contain("items"));
            Assert.That(query.Queries, Is.Empty);
        });
    }

    [Test]
    public async Task TeacherOfK_Gets200_WithTheAllowlistedPage_AndExactlyOneViewRecord()
    {
        var query = new ScriptedCourseAuditQueryService();
        var writer = new ValidatingTrustedAuditWriter();
        using var factory = WithTeacherOfK(query, writer);

        using var response = await Get(factory, CourseK, Teacher, "?type=access.denied&outcome=denied&page=1&pageSize=50");
        var body = await Json(response);
        var item = body.GetProperty("items")[0];
        var (draft, context) = writer.Written.Single();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(query.Queries.Single(), Is.EqualTo(new CourseAuditQuery(
                CourseK, "access.denied", "denied", Page: "1", PageSize: "50")));
            Assert.That(item.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[]
            {
                "id", "occurredAt", "eventType", "outcome", "actorKind", "actorId", "resourceType", "resourceId",
                "correlationId"
            }));
            Assert.That(item.GetProperty("outcome").GetString(), Is.EqualTo("denied"));
            Assert.That(item.GetProperty("actorKind").GetString(), Is.EqualTo("anonymous"));
            Assert.That(body.GetProperty("hasNextPage").GetBoolean(), Is.False);
            Assert.That(body.TryGetProperty("total", out _), Is.False, "No total count (AUD-06, D5).");

            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AuditEventsViewed));
            Assert.That(draft.CourseId, Is.EqualTo(CourseK));
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(TeacherId)));
            Assert.That(context.CorrelationId, Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["filterType"] = "access.denied",
                ["filterOutcome"] = "denied",
                ["filterFrom"] = "2026-11-01T00:00:00.0000000Z",
                ["filterTo"] = "2026-11-08T00:00:00.0000000Z",
                ["resultCount"] = "1"
            }));
        });
    }

    [Test]
    public async Task TeacherOfK_Gets403_ForL_LooksLikeAnUnknownCourse_AndNothingIsReadOrRecorded()
    {
        var query = new ScriptedCourseAuditQueryService();
        var writer = new ValidatingTrustedAuditWriter();
        using var factory = WithTeacherOfK(query, writer);
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher, accountId: OtherAccountId, courseId: CourseL));

        using var existing = await Get(factory, CourseL, Teacher);
        using var unknown = await Get(factory, CourseM, Teacher);
        var existingBody = await Json(existing);
        var unknownBody = await Json(unknown);

        Assert.Multiple(() =>
        {
            Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(existingBody.GetProperty("title").GetString(), Is.EqualTo(unknownBody.GetProperty("title").GetString()));
            Assert.That(existingBody.EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(unknownBody.EnumerateObject().Select(p => p.Name)));
            Assert.That(query.Queries, Is.Empty);
            Assert.That(writer.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task CallerWithoutTheRight_Gets403_EvenWithInvalidFilters()
    {
        // Authorization comes first: an outsider learns nothing from filter validation.
        var query = new ScriptedCourseAuditQueryService();
        using var factory = WithTeacherOfK(query);

        using var response = await Get(factory, CourseL, Teacher, "?page=0&type=nope");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(query.Queries, Is.Empty);
        });
    }

    [TestCase("?page=0", "page:OutOfRange")]
    [TestCase("?pageSize=0", "pageSize:OutOfRange")]
    [TestCase("?pageSize=101", "pageSize:OutOfRange")]
    [TestCase("?page=102&pageSize=100", "page:OutOfRange")]
    [TestCase("?page=abc", "page:InvalidFormat")]
    [TestCase("?from=2026-11-08T00:00:00Z&to=2026-11-08T00:00:00Z", "range:OutOfRange")]
    [TestCase("?from=2026-11-08T00:00:00Z&to=2026-11-01T00:00:00Z", "range:OutOfRange")]
    [TestCase("?from=2026-10-01T00:00:00Z&to=2026-11-02T00:00:00Z", "range:OutOfRange")]
    [TestCase("?from=2026-11-01", "from:InvalidFormat")]
    [TestCase("?type=unknown.event", "type:UnknownValue")]
    [TestCase("?outcome=Denied", "outcome:UnknownValue")]
    [TestCase("?actorId=not-a-guid", "actorId:InvalidFormat")]
    [TestCase("?correlationId=a%20b", "correlationId:InvalidFormat")]
    [TestCase("?type=access.denied&type=auth.login", "type:InvalidFormat")]
    public async Task InvalidFilter_Gets400_WithFieldAndCode_BeforeAnyDatabaseCall(string query, string expected)
    {
        using var factory = WithTeacherOfK();

        using var response = await Get(factory, CourseK, Teacher, query);
        var body = await Json(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(Errors(body), Is.EqualTo(new[] { expected }));
            Assert.That(body.TryGetProperty("items", out _), Is.False);
        });
    }

    [TestCase("?pageSize=1")]
    [TestCase("?pageSize=100")]
    [TestCase("?page=101&pageSize=100")]
    [TestCase("?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z")]
    [TestCase("?from=2026-11-01T01:00:00%2B01:00")]
    public async Task BoundaryValues_PassValidation_AndReachStorage(string query)
    {
        // The store is unreachable, so an accepted filter ends in 503 (never 400, never data).
        using var factory = WithTeacherOfK();

        using var response = await Get(factory, CourseK, Teacher, query);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task HostileFilterValues_AreNeverEchoed_InTheResponseOrTheLog()
    {
        using var factory = WithTeacherOfK();

        using var response = await Get(factory, CourseK, Teacher,
            "?type=%3Cscript%3Ealert(1)%3C%2Fscript%3E&actorId=1'%20OR%20'1'%3D'1");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(text, Does.Not.Contain("script").And.Not.Contain("OR"));
            Assert.That(factory.Logs.AllText, Does.Not.Contain("<script>").And.Not.Contain("' OR '"));
        });
    }

    [Test]
    public async Task StorageDown_Gets503_WithoutDataOrInternalDetails()
    {
        using var factory = WithTeacherOfK();

        using var response = await Get(factory, CourseK, Teacher);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(text, Does.Not.Contain("items").And.Not.Contain("Npgsql").And.Not.Contain("127.0.0.1"));
            Assert.That(factory.Logs.Entries.Any(entry => entry.EventId.Id == CourseAuditEndpointHandler.QueryUnavailableEventId),
                Is.True);
        });
    }

    [TestCase(false, TestName = "ViewRecordUnavailable_Gets503_AndNoEventIsSent")]
    [TestCase(true, TestName = "ViewRecordWriterThrows_Gets503_AndNoEventIsSent")]
    public async Task ViewRecordFails_Gets503_AndNoEventIsSent(bool throws)
    {
        var writer = new ValidatingTrustedAuditWriter
        {
            Handler = (_, _, _) => throws
                ? throw new InvalidOperationException("Host=db;Password=s3cret")
                : ValueTask.FromResult(AuditWriteResult.Unavailable())
        };
        using var factory = WithTeacherOfK(new ScriptedCourseAuditQueryService(), writer);

        using var response = await Get(factory, CourseK, Teacher);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(text, Does.Not.Contain(ScriptedCourseAuditQueryService.EventId.ToString()));
            Assert.That(text, Does.Not.Contain("items").And.Not.Contain("s3cret"));
            Assert.That(factory.Logs.Entries.Any(entry => entry.EventId.Id == CourseAuditViewRecorder.AlarmEventId), Is.True);
            Assert.That(factory.Logs.AllText, Does.Not.Contain("s3cret"));
        });
    }

    [Test]
    public async Task QueryServiceThatThrows_Gets503()
    {
        var query = new ScriptedCourseAuditQueryService { Handler = _ => throw new InvalidOperationException("boom") };
        using var factory = WithTeacherOfK(query);

        using var response = await Get(factory, CourseK, Teacher);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task RevokedMembership_AppliesFromTheNextRequest()
    {
        using var factory = WithTeacherOfK(new ScriptedCourseAuditQueryService(), new ValidatingTrustedAuditWriter());

        using var before = await Get(factory, CourseK, Teacher);
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher) with { Status = MembershipStatus.Left });
        using var after = await Get(factory, CourseK, Teacher);

        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [TestCase(new string[0], HttpStatusCode.Forbidden, TestName = "Assistant_WithoutDelegation_Gets403")]
    [TestCase(new[] { "attempt.review" }, HttpStatusCode.Forbidden, TestName = "Assistant_WithAnotherDelegation_Gets403")]
    [TestCase(new[] { "audit.events.read" }, HttpStatusCode.OK, TestName = "Assistant_WithAuditReadDelegation_Gets200")]
    public async Task Assistant_NeedsTheExactDelegation(string[] delegated, HttpStatusCode expected)
    {
        using var factory = new CourseAuditEndpointHostFactory
        {
            Query = new ScriptedCourseAuditQueryService(),
            Writer = new ValidatingTrustedAuditWriter()
        };
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: delegated));

        using var response = await Get(factory, CourseK, Teacher);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task StudentAndBlockedTeacher_Get403()
    {
        using var factory = new CourseAuditEndpointHostFactory { Query = new ScriptedCourseAuditQueryService() };
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student));
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher, accountId: OtherAccountId));

        using var student = await Get(factory, CourseK, Teacher);
        using var blocked = await Get(factory, CourseK, "blocked:" + OtherAccountId.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(student.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task WithoutTestSession_TheSessionCheckStillApplies()
    {
        // The production session middleware stays active: no valid session cookie, no data.
        using var factory = WithTeacherOfK(new ScriptedCourseAuditQueryService(), new ValidatingTrustedAuditWriter());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Cookie");
        var request = new HttpRequestMessage(HttpMethod.Get, CourseAuditEndpointRoute.For(CourseK));
        request.Headers.Add(HeaderTestSubjectProvider.HeaderName, Teacher);

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
