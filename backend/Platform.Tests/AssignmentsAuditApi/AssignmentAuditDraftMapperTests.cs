using Platform.Access.Domain.ValueObjects;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Audit.Contracts;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 / ASN-06 TIM 15 (celina 5): allowlist i prevođenje u nacrt Audit modula (AUD-01, AUD-03).
[TestFixture]
public sealed class AssignmentAuditDraftMapperTests
{
    private static readonly Guid AssignmentId = Guid.Parse("a5500000-0000-4000-8000-000000000001");
    private static readonly Guid CourseGuid = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000015");
    private static readonly DateTimeOffset Previous = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset Next = new(2026, 11, 20, 23, 59, 0, TimeSpan.FromHours(1));

    private static readonly string[] ForbiddenKeyParts =
        ["password", "passwd", "secret", "token", "authorization", "apikey", "cookie", "session"];

    [TestCase("student 1")]
    [TestCase("student-1\n")]
    [TestCase("<script>alert(1)</script>")]
    [TestCase("session=abc;token=xyz")]
    public void Sanitize_DropsInvalidStudentId(string studentId)
    {
        var safe = AssignmentAuditDraftMapper.Sanitize(Draft(AssignmentAuditOutcome.Failure, studentId: studentId));

        Assert.That(safe.StudentId, Is.Null);
    }

    [TestCase("Not A Code")]
    [TestCase("forbidden; drop table")]
    [TestCase("x_very_long_reason_code_over_thirty_two_chars")]
    public void Sanitize_DropsInvalidReasonCode(string reasonCode)
    {
        var safe = AssignmentAuditDraftMapper.Sanitize(Draft(AssignmentAuditOutcome.Denied, reasonCode: reasonCode));

        Assert.That(safe.ReasonCode, Is.Null);
    }

    [Test]
    public void Sanitize_KeepsReasonOnlyForDeniedOrFailure_AndAttemptOnlyForSuccess()
    {
        var success = AssignmentAuditDraftMapper.Sanitize(new AssignmentAuditDraft(
            AssignmentAuditActions.StartAttempt, AssignmentAuditOutcome.Success, AssignmentId,
            ReasonCode: "forbidden", AttemptId: Guid.NewGuid()));
        var failure = AssignmentAuditDraftMapper.Sanitize(new AssignmentAuditDraft(
            AssignmentAuditActions.StartAttempt, AssignmentAuditOutcome.Failure, AssignmentId,
            ReasonCode: "attempt_limit_reached", AttemptId: Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(success.ReasonCode, Is.Null);
            Assert.That(success.AttemptId, Is.Not.Null);
            Assert.That(failure.ReasonCode, Is.EqualTo("attempt_limit_reached"));
            Assert.That(failure.AttemptId, Is.Null);
        });
    }

    [Test]
    public void Sanitize_RejectsUnknownAction()
    {
        var draft = new AssignmentAuditDraft("assignment.delete.all", AssignmentAuditOutcome.Success, AssignmentId);

        Assert.Throws<ArgumentException>(() => AssignmentAuditDraftMapper.Sanitize(draft));
    }

    [Test]
    public void ToPlatformDraft_DeadlineExtension_MapsResourceCourseAndUtcTimestamps()
    {
        var draft = AssignmentAuditDraftMapper.ToPlatformDraft(
            Draft(AssignmentAuditOutcome.Success, courseId: CourseGuid.ToString("D")));

        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.CourseId, Is.EqualTo(CourseGuid));
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, "student-1")));
            Assert.That(draft.Details!["previousDueAt"], Is.EqualTo("2026-11-09T23:59:59Z"));
            Assert.That(draft.Details["newDueAt"], Is.EqualTo("2026-11-20T22:59:00Z"));
            Assert.That(draft.Details.ContainsKey("reasonCode"), Is.False);
        });
    }

    [Test]
    public void ToPlatformDraft_WhenCourseIdIsNotGuid_OmitsCourseInsteadOfInventingOne()
    {
        var draft = AssignmentAuditDraftMapper.ToPlatformDraft(Draft(AssignmentAuditOutcome.Failure, courseId: "course-oib"));

        Assert.That(draft.CourseId, Is.Null);
    }

    [Test]
    public void ToPlatformDraft_StartAttempt_HasAttemptIdAndNoResource()
    {
        var attemptId = Guid.NewGuid();
        var draft = AssignmentAuditDraftMapper.ToPlatformDraft(new AssignmentAuditDraft(
            AssignmentAuditActions.StartAttempt, AssignmentAuditOutcome.Success, AssignmentId,
            CourseGuid.ToString("D"), "student-1", AttemptId: attemptId));

        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.AttemptStarted));
            Assert.That(draft.Resource, Is.Null);
            Assert.That(draft.Details!["attemptId"], Is.EqualTo(attemptId.ToString("D")));
        });
    }

    [Test]
    public void ToAccessDeniedDraft_IsAcceptedByCurrentAuditCatalog()
    {
        var draft = AssignmentAuditDraftMapper.ToAccessDeniedDraft(
            Draft(AssignmentAuditOutcome.Denied, courseId: CourseGuid.ToString("D"), reasonCode: "forbidden"));

        Assert.That(AuditEventCatalog.TryGet(draft.EventType, out var schema), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(schema!.ActorsByOutcome.ContainsKey(draft.Outcome), Is.True);
            Assert.That(draft.Details!.Keys, Is.SubsetOf(schema.Details.Keys));
            Assert.That(ActionId.IsValid(draft.Details["action"]), Is.True);
            Assert.That(draft.Details["action"], Is.EqualTo(AssignmentAuditActions.ExtendDeadline));
            Assert.That(draft.Details["denialReason"], Is.EqualTo("policy_denied"));
            Assert.That(draft.CourseId, Is.EqualTo(CourseGuid));
        });
    }

    [Test]
    public void ToAccessDeniedDraft_OnlyForDenied()
    {
        Assert.Throws<ArgumentException>(
            () => AssignmentAuditDraftMapper.ToAccessDeniedDraft(Draft(AssignmentAuditOutcome.Failure)));
    }

    [TestCase(AssignmentAuditActions.StartAttempt)]
    [TestCase(AssignmentAuditActions.ExtendDeadline)]
    public void Actions_AreValidAccessActionIds(string action)
    {
        Assert.That(ActionId.IsValid(action), Is.True);
    }

    // Kad tim 12 doda predložene tipove u katalog, ovaj test proverava da im naši nacrti odgovaraju.
    [TestCase(AssignmentAuditOutcome.Success)]
    [TestCase(AssignmentAuditOutcome.Denied)]
    [TestCase(AssignmentAuditOutcome.Failure)]
    public void ToPlatformDraft_MatchesCatalogSchema_WhenTeam12AddsTheType(AssignmentAuditOutcome outcome)
    {
        var draft = AssignmentAuditDraftMapper.ToPlatformDraft(
            Draft(outcome, courseId: CourseGuid.ToString("D"), reasonCode: "forbidden"));

        if (!AuditEventCatalog.TryGet(draft.EventType, out var schema))
        {
            Assert.Pass($"{draft.EventType} is still a proposal for the Audit catalog (Team 12).");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(schema.ActorsByOutcome.ContainsKey(draft.Outcome), Is.True);
            Assert.That(draft.Details!.Keys, Is.SubsetOf(schema.Details.Keys));
        });
    }

    [Test]
    public void Drafts_NeverUseSecretLikeDetailKeys()
    {
        var drafts = new[]
        {
            AssignmentAuditDraftMapper.ToPlatformDraft(Draft(AssignmentAuditOutcome.Success)),
            AssignmentAuditDraftMapper.ToPlatformDraft(Draft(AssignmentAuditOutcome.Failure, reasonCode: "invalid_deadline")),
            AssignmentAuditDraftMapper.ToPlatformDraft(new AssignmentAuditDraft(
                AssignmentAuditActions.StartAttempt, AssignmentAuditOutcome.Success, AssignmentId, AttemptId: Guid.NewGuid())),
            AssignmentAuditDraftMapper.ToAccessDeniedDraft(Draft(AssignmentAuditOutcome.Denied)),
        };

        var keys = drafts.SelectMany(d => d.Details?.Keys ?? Enumerable.Empty<string>()).ToList();

        Assert.That(
            keys.Where(key => ForbiddenKeyParts.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase))),
            Is.Empty);
    }

    private static AssignmentAuditDraft Draft(
        AssignmentAuditOutcome outcome,
        string? courseId = null,
        string? studentId = "student-1",
        string? reasonCode = null) =>
        new(
            AssignmentAuditActions.ExtendDeadline,
            outcome,
            AssignmentId,
            courseId,
            studentId,
            reasonCode,
            Previous,
            Next);
}
