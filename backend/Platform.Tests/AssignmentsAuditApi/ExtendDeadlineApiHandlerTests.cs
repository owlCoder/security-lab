using Microsoft.AspNetCore.Http;
using Platform.Api.Modules.Assignments.Api;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-06 TIM 15 (celina 5): HTTP mapiranje ishoda „produži rok studentu”.
// Koristi se pravi ExtendDeadlineService (član 3) i naš audit adapter.
[TestFixture]
public sealed class ExtendDeadlineApiHandlerTests
{
    private const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private const string Student = "22222222-2222-2222-2222-222222222222";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 12, 23, 59, 0, TimeSpan.Zero);

    private InMemoryAssignmentRepository _repository = null!;
    private InMemoryDeadlineExtensionStore _store = null!;
    private RecordingAssignmentAuditWriter _audit = null!;
    private ExtendDeadlineApiHandler _handler = null!;
    private Assignment _assignment = null!;

    [SetUp]
    public void SetUp()
    {
        _assignment = new Assignment(Guid.NewGuid(), Course, "revision-1", null, Due, 3);
        _repository = new InMemoryAssignmentRepository();
        _repository.Add(_assignment);

        var authorization = new AllowListDeadlineAuthorization();
        authorization.Allow(Teacher, Course);
        var enrollment = new AllowListEnrollment();
        enrollment.Enroll(Student, Course);

        _store = new InMemoryDeadlineExtensionStore();
        _audit = new RecordingAssignmentAuditWriter();

        var service = new ExtendDeadlineService(
            _repository, authorization, enrollment, _store, new DeadlineExtensionAuditAdapter(_audit),
            new FixedTimeProvider(Now));

        _handler = new ExtendDeadlineApiHandler(service, new CapturingLogger<ExtendDeadlineApiHandler>());
    }

    [TestCase("2026-11-20T23:59:00Z")]
    [TestCase("2026-11-21T00:59:00+01:00")]
    [TestCase("2026-11-20T23:59:00.1234567Z")]
    public void TryParseDeadline_AcceptsExplicitZone(string value)
    {
        Assert.That(ExtendDeadlineApiHandler.TryParseDeadline(value, out var parsed), Is.True);
        Assert.That(parsed.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("2026-11-20T23:59:00")]
    [TestCase("20.11.2026. 23:59")]
    [TestCase("2026-11-20T23:59:00Z\n")]
    [TestCase("2026-13-40T99:99:00Z")]
    [TestCase("2026-11-20T23:59:00.123456789012345678901234567890Z")]
    public void TryParseDeadline_RejectsMissingZoneOrGarbage(string? value)
    {
        Assert.That(ExtendDeadlineApiHandler.TryParseDeadline(value, out _), Is.False);
    }

    [Test]
    public async Task Extend_ByCourseTeacher_Returns200WithNewAndPreviousDeadline()
    {
        var result = await _handler.HandleAsync(Teacher, _assignment.Id, Student, Request("2026-11-20T23:59:00Z"));

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        var body = (ExtendDeadlineResponse)result.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.StudentId, Is.EqualTo(Student));
            Assert.That(body.DueAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 20, 23, 59, 0, TimeSpan.Zero)));
            Assert.That(body.PreviousDueAtUtc, Is.EqualTo(Due));
            Assert.That(body.ExtendedAtUtc, Is.EqualTo(Now));
            Assert.That(_store.SaveCount, Is.EqualTo(1));
        });
    }

    [TestCase(Student)]
    [TestCase("44444444-4444-4444-4444-444444444444")]
    public async Task Extend_ByStudentOrOtherTeacher_Returns403_AndChangesNothing(string actor)
    {
        var result = await _handler.HandleAsync(actor, _assignment.Id, Student, Request("2026-11-20T23:59:00Z"));

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(((AssignmentErrorResponse)result.Body).Code, Is.EqualTo(AssignmentErrorCodes.Forbidden));
            Assert.That(_store.SaveCount, Is.EqualTo(0));
            Assert.That(_audit.Drafts.Single().Outcome, Is.EqualTo(AssignmentAuditOutcome.Denied));
        });
    }

    [Test]
    public async Task Extend_UnknownAssignment_Returns403LikeForbidden()
    {
        var result = await _handler.HandleAsync(Teacher, Guid.NewGuid(), Student, Request("2026-11-20T23:59:00Z"));

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [TestCase("2026-11-11T10:00:00Z")]
    [TestCase("2026-11-12T23:59:00Z")]
    public async Task Extend_NotLaterThanCurrentDeadline_Returns400(string newDue)
    {
        var result = await _handler.HandleAsync(Teacher, _assignment.Id, Student, Request(newDue));

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(((AssignmentErrorResponse)result.Body).Code, Is.EqualTo(AssignmentErrorCodes.InvalidDeadline));
            Assert.That(_audit.Drafts.Single().Outcome, Is.EqualTo(AssignmentAuditOutcome.Failure));
        });
    }

    [Test]
    public async Task Extend_InvalidStudentId_Returns400InvalidStudent()
    {
        var result = await _handler.HandleAsync(Teacher, _assignment.Id, "bad student!", Request("2026-11-20T23:59:00Z"));

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(((AssignmentErrorResponse)result.Body).Code, Is.EqualTo(AssignmentErrorCodes.InvalidStudent));
    }

    [Test]
    public async Task Extend_StudentNotEnrolled_Returns409()
    {
        var result = await _handler.HandleAsync(Teacher, _assignment.Id, "student-x", Request("2026-11-20T23:59:00Z"));

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
    }

    [Test]
    public async Task Extend_WithoutActor_Returns401_AndServiceIsNotCalled()
    {
        var result = await _handler.HandleAsync(null, _assignment.Id, Student, Request("2026-11-20T23:59:00Z"));

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(_store.SaveCount, Is.EqualTo(0));
            Assert.That(_audit.Drafts, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("2026-11-20T23:59:00")]
    public async Task Extend_MissingOrZonelessDeadline_Returns400(string? value)
    {
        var request = value is null ? null : Request(value);

        var result = await _handler.HandleAsync(Teacher, _assignment.Id, Student, request);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(_store.SaveCount, Is.EqualTo(0));
    }

    private static ExtendDeadlineRequest Request(string value) => new(value);
}
