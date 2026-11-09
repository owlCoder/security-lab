using System.Globalization;
using System.Net;
using System.Text.Json;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 / ASN-06 TIM 15 (celina 5): kontroleri kroz ceo HTTP pipeline.
// Proverava: 401 bez sesije, akter samo iz sesije (nikad iz tela), isti 404 za zabranjeno i
// nepostojeće, 409 iznad limita, 403 za studenta na ASN-06, korelacioni ID u odgovoru i u auditu.
[TestFixture]
public sealed class AssignmentsHttpTests
{
    private const string Course = AssignmentsHostFactory.Course;
    private const string Teacher = AssignmentsHostFactory.Teacher;
    private const string Student = AssignmentsHostFactory.Student;
    private const string Outsider = AssignmentsHostFactory.Outsider;
    private const string BlockedStudent = AssignmentsHostFactory.BlockedStudent;

    private AssignmentsHostFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new AssignmentsHostFactory();
        _client = _factory.CreateClient();

        _factory.StartAuthorization.Allow(Student, Course);
        _factory.StartAuthorization.Allow(BlockedStudent, Course);
        _factory.DeadlineAuthorization.Allow(Teacher, Course);
        _factory.Enrollment.Enroll(Student, Course);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public void SetUp() => _factory.Audit.Clear();

    // ---------- ASN-04: započni pokušaj ----------

    [Test]
    public async Task StartAttempt_WithoutSession_Returns401()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.StartAttempt(assignment.Id, user: null, NewKey()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_factory.Attempts.Count(assignment.Id, Student), Is.EqualTo(0));
    }

    [Test]
    public async Task StartAttempt_EnrolledStudent_Returns201_AndAuditCarriesSessionActorAndCorrelation()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.StartAttempt(assignment.Id, Student, NewKey()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var attemptId = (await Json(response)).GetProperty("attemptId").GetGuid();
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        var (draft, context) = _factory.Audit.Calls.Single();

        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.AttemptStarted));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.Details!["attemptId"], Is.EqualTo(attemptId.ToString("D")));
            Assert.That(draft.CourseId, Is.EqualTo(Guid.Parse(Course)));
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(Guid.Parse(Student))));
            Assert.That(context.CorrelationId, Is.EqualTo(correlationId));
        });
    }

    [Test]
    public async Task StartAttempt_AboveLimit_Returns409_AndKeepsExistingAttempts()
    {
        var assignment = AddAssignment(maxAttempts: 1);
        using var first = await _client.SendAsync(AssignmentsHostFactory.StartAttempt(assignment.Id, Student, NewKey()));

        using var second = await _client.SendAsync(AssignmentsHostFactory.StartAttempt(assignment.Id, Student, NewKey()));

        var code = (await Json(second)).GetProperty("code").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo("attempt_limit_reached"));
            Assert.That(_factory.Attempts.Count(assignment.Id, Student), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartAttempt_OutsiderAndUnknownAssignment_GetIdenticalResponse()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var forbidden = await _client.SendAsync(AssignmentsHostFactory.StartAttempt(assignment.Id, Outsider, NewKey()));
        using var unknown = await _client.SendAsync(AssignmentsHostFactory.StartAttempt(Guid.NewGuid(), Student, NewKey()));

        var forbiddenBody = await forbidden.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(forbiddenBody, Is.EqualTo(unknownBody));
            // Odbijanje ostaje zabeleženo i pre nego što tim 12 doda assignment.* u katalog.
            Assert.That(_factory.Audit.Calls.Any(c =>
                c.Draft.EventType == AuditEventTypes.AccessDenied
                && c.Context.Actor == AuditActor.ForUser(Guid.Parse(Outsider))
                && c.Draft.Details!["action"] == AssignmentAuditActions.StartAttempt), Is.True);
        });
    }

    [Test]
    public async Task StartAttempt_BlockedAccount_Returns401EvenWithValidSession()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.StartAttempt(assignment.Id, BlockedStudent, NewKey()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_factory.Attempts.Count(assignment.Id, BlockedStudent), Is.EqualTo(0));
    }

    [Test]
    public async Task StartAttempt_WithoutIdempotencyKey_Returns400()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.StartAttempt(assignment.Id, Student, idempotencyKey: null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // ---------- ASN-06: produži rok ----------

    [Test]
    public async Task ExtendDeadline_CourseTeacher_Returns200()
    {
        var assignment = AddAssignment(maxAttempts: 2);
        var newDue = Iso(DateTimeOffset.UtcNow.AddDays(14));

        using var response = await _client.SendAsync(AssignmentsHostFactory.ExtendDeadline(
            assignment.Id, Student, Teacher, $"{{\"newDueAtUtc\":\"{newDue}\"}}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await Json(response);
        var stored = await _factory.DeadlineStore.GetAsync(assignment.Id, Student);
        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("studentId").GetString(), Is.EqualTo(Student));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.ExtendedById, Is.EqualTo(Teacher));
        });
    }

    [Test]
    public async Task ExtendDeadline_Student_Returns403_EvenWhenBodyClaimsTeacherActor()
    {
        var assignment = AddAssignment(maxAttempts: 2);
        var newDue = Iso(DateTimeOffset.UtcNow.AddDays(14));
        var json = $"{{\"newDueAtUtc\":\"{newDue}\",\"actorId\":\"{Teacher}\",\"courseId\":\"{Course}\"}}";

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.ExtendDeadline(assignment.Id, Student, Student, json));

        var denied = _factory.Audit.Calls.Last();
        var code = (await Json(response)).GetProperty("code").GetString();
        var stored = await _factory.DeadlineStore.GetAsync(assignment.Id, Student);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(code, Is.EqualTo("forbidden"));
            Assert.That(stored, Is.Null);
            Assert.That(denied.Draft.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(denied.Context.Actor, Is.EqualTo(AuditActor.ForUser(Guid.Parse(Student))));
        });
    }

    [Test]
    public async Task ExtendDeadline_WithoutSession_Returns401()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(AssignmentsHostFactory.ExtendDeadline(
            assignment.Id, Student, user: null, "{\"newDueAtUtc\":\"2030-01-01T00:00:00Z\"}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("{\"newDueAtUtc\":\"2030-01-01T00:00:00\"}")]
    [TestCase("{\"newDueAtUtc\":\"sutra\"}")]
    [TestCase("{}")]
    public async Task ExtendDeadline_InvalidDeadline_Returns400(string json)
    {
        var assignment = AddAssignment(maxAttempts: 2);

        using var response = await _client.SendAsync(
            AssignmentsHostFactory.ExtendDeadline(assignment.Id, Student, Teacher, json));

        var stored = await _factory.DeadlineStore.GetAsync(assignment.Id, Student);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(stored, Is.Null);
    }

    // ---------- podrazumevana kompozicija (bez adaptera članova 1 i 4) ----------

    [Test]
    public async Task DefaultComposition_HostStarts_AndEverythingIsDenied()
    {
        using var factory = new AssignmentsHostFactory(useFakePorts: false);
        using var client = factory.CreateClient();
        var assignmentId = Guid.NewGuid();

        using var start = await client.SendAsync(AssignmentsHostFactory.StartAttempt(assignmentId, Student, NewKey()));
        using var extend = await client.SendAsync(AssignmentsHostFactory.ExtendDeadline(
            assignmentId, Student, Teacher, "{\"newDueAtUtc\":\"2030-01-01T00:00:00Z\"}"));

        Assert.Multiple(() =>
        {
            Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(extend.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    private Assignment AddAssignment(int maxAttempts)
    {
        var assignment = new Assignment(
            Guid.NewGuid(), Course, "revision-1", null, DateTimeOffset.UtcNow.AddDays(7), maxAttempts);
        _factory.Assignments.Add(assignment);
        return assignment;
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
