using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Platform.Api.Modules.Assignments.Api;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 TIM 15 (celina 5): HTTP mapiranje ishoda „započni pokušaj” i audit svakog ishoda.
// Koristi se pravi StartAttemptService (član 2); portovi su test dvojnici.
[TestFixture]
public sealed class StartAttemptApiHandlerTests
{
    private const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    private const string Student = "22222222-2222-2222-2222-222222222222";
    private const string Outsider = "33333333-3333-3333-3333-333333333333";

    private InMemoryAssignmentRepository _repository = null!;
    private CountingAttemptStore _attempts = null!;
    private AllowListStartAuthorization _authorization = null!;
    private RecordingAssignmentAuditWriter _audit = null!;
    private CapturingLogger<StartAttemptApiHandler> _logger = null!;
    private StartAttemptApiHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryAssignmentRepository();
        _attempts = new CountingAttemptStore();
        _authorization = new AllowListStartAuthorization();
        _authorization.Allow(Student, Course);
        _audit = new RecordingAssignmentAuditWriter();
        _logger = new CapturingLogger<StartAttemptApiHandler>();
        _handler = new StartAttemptApiHandler(
            new StartAttemptService(_repository, _attempts, _authorization), _repository, _audit, _logger);
    }

    [Test]
    public async Task Start_WhenAllowedAndBelowLimit_Returns201AndAuditsSuccess()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        var result = await _handler.HandleAsync(Student, assignment.Id, NewKey());

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        var attemptId = ((StartAttemptResponse)result.Body).AttemptId;
        var draft = _audit.Drafts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(draft.Action, Is.EqualTo(AssignmentAuditActions.StartAttempt));
            Assert.That(draft.Outcome, Is.EqualTo(AssignmentAuditOutcome.Success));
            Assert.That(draft.AttemptId, Is.EqualTo(attemptId));
            Assert.That(draft.CourseId, Is.EqualTo(Course));
            Assert.That(draft.StudentId, Is.EqualTo(Student));
        });
    }

    [Test]
    public async Task Start_AboveLimit_Returns409_DoesNotCreateAttempt_AndAuditsFailure()
    {
        var assignment = AddAssignment(maxAttempts: 1);
        await _handler.HandleAsync(Student, assignment.Id, NewKey());

        var result = await _handler.HandleAsync(Student, assignment.Id, NewKey());

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(Code(result), Is.EqualTo(AssignmentErrorCodes.AttemptLimitReached));
            Assert.That(_attempts.Count(assignment.Id, Student), Is.EqualTo(1));
            Assert.That(_audit.Drafts[^1].Outcome, Is.EqualTo(AssignmentAuditOutcome.Failure));
            Assert.That(_audit.Drafts[^1].ReasonCode, Is.EqualTo(StartAttemptReasonCodes.AttemptLimitReached));
        });
    }

    [Test]
    public async Task Start_SameIdempotencyKeyTwice_ReturnsSameAttemptAndUsesOneSlot()
    {
        var assignment = AddAssignment(maxAttempts: 3);
        var key = NewKey();

        var first = await _handler.HandleAsync(Student, assignment.Id, key);
        var second = await _handler.HandleAsync(Student, assignment.Id, key);

        Assert.That(((StartAttemptResponse)second.Body).AttemptId,
            Is.EqualTo(((StartAttemptResponse)first.Body).AttemptId));
        Assert.That(_attempts.Count(assignment.Id, Student), Is.EqualTo(1));
    }

    [Test]
    public async Task Start_ForbiddenAndUnknown_ReturnSameResponse_ButDifferentAuditOutcome()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        var forbidden = await _handler.HandleAsync(Outsider, assignment.Id, NewKey());
        var unknown = await _handler.HandleAsync(Student, Guid.NewGuid(), NewKey());

        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(JsonSerializer.Serialize(forbidden.Body), Is.EqualTo(JsonSerializer.Serialize(unknown.Body)));
            Assert.That(_audit.Drafts[0].Outcome, Is.EqualTo(AssignmentAuditOutcome.Denied));
            Assert.That(_audit.Drafts[0].ReasonCode, Is.EqualTo(StartAttemptReasonCodes.Forbidden));
            Assert.That(_audit.Drafts[1].Outcome, Is.EqualTo(AssignmentAuditOutcome.Failure));
            Assert.That(_audit.Drafts[1].ReasonCode, Is.EqualTo(StartAttemptReasonCodes.NotFound));
            Assert.That(_attempts.Count(assignment.Id, Outsider), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Start_AfterDeadline_Returns409Closed()
    {
        var assignment = new Assignment(
            Guid.NewGuid(), Course, "revision-1", DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1), 3);
        _repository.Add(assignment);

        var result = await _handler.HandleAsync(Student, assignment.Id, NewKey());

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(Code(result), Is.EqualTo(AssignmentErrorCodes.AssignmentClosed));
            Assert.That(_audit.Drafts.Single().ReasonCode, Is.EqualTo(StartAttemptReasonCodes.NotAvailable));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task Start_WithoutValidIdempotencyKey_Returns400_AndDoesNothing(string? key)
    {
        var assignment = AddAssignment(maxAttempts: 2);

        var result = await _handler.HandleAsync(Student, assignment.Id, key);

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(Code(result), Is.EqualTo(AssignmentErrorCodes.InvalidRequestId));
            Assert.That(_attempts.Count(assignment.Id, Student), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Start_WithoutActor_Returns401_AndAuditsDenied()
    {
        var assignment = AddAssignment(maxAttempts: 2);

        var result = await _handler.HandleAsync(null, assignment.Id, NewKey());

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(_audit.Drafts.Single().Outcome, Is.EqualTo(AssignmentAuditOutcome.Denied));
            Assert.That(_audit.Drafts.Single().ReasonCode, Is.EqualTo(StartAttemptReasonCodes.Unauthenticated));
        });
    }

    [Test]
    public async Task Start_WhenStoreFails_Returns503WithoutInternalDetails()
    {
        var assignment = AddAssignment(maxAttempts: 2);
        _attempts.ThrowOnStart = true;

        var result = await _handler.HandleAsync(Student, assignment.Id, NewKey());

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(JsonSerializer.Serialize(result.Body), Does.Not.Contain("hunter2"));
            Assert.That(_logger.AllText, Does.Not.Contain("hunter2"));
            Assert.That(_audit.Drafts.Single().ReasonCode, Is.EqualTo(StartAttemptReasonCodes.Unavailable));
        });
    }

    [Test]
    public async Task Start_WhenAuditFails_TheDecisionStaysTheSame()
    {
        var assignment = AddAssignment(maxAttempts: 2);
        _audit.Result = false;

        var forbidden = await _handler.HandleAsync(Outsider, assignment.Id, NewKey());

        Assert.That(forbidden.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    // Ugovor sa članom 2: handler prepoznaje limit po poruci servisa.
    [Test]
    public void LimitMessage_MatchesStartAttemptService()
    {
        var assignment = AddAssignment(maxAttempts: 1);
        var service = new StartAttemptService(_repository, _attempts, _authorization);
        Assert.DoesNotThrowAsync(async () => await service.StartAttemptAsync(assignment.Id, Student, Guid.NewGuid()));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartAttemptAsync(assignment.Id, Student, Guid.NewGuid()));

        Assert.That(exception!.Message, Is.EqualTo(StartAttemptApiHandler.LimitReachedMessage));
    }

    private Assignment AddAssignment(int maxAttempts)
    {
        var assignment = new Assignment(
            Guid.NewGuid(), Course, "revision-1", null, DateTimeOffset.UtcNow.AddDays(7), maxAttempts);
        _repository.Add(assignment);
        return assignment;
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static string Code(AssignmentApiResult result) => ((AssignmentErrorResponse)result.Body).Code;
}
