using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

// T03 (AUD-01): a draft outside the catalog is rejected with a stable code.
[TestFixture]
public sealed class AuditDraftValidatorTests
{
    private readonly AuditDraftValidator _validator = new();

    [TestCase("policy_denied")]
    [TestCase("policy_error")]
    public void Validate_AccessDenial_AcceptsExpectedRejectionAndEvaluationError(string reason)
    {
        var draft = new AuditEventDraft(AuditEventTypes.AccessDenied, AuditOutcome.Denied,
            Details: Details(("action", "revision.read"), ("denialReason", reason)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    private static IEnumerable<TestCaseData> ValidExamples() =>
        AuditDraftExamples.ValidPerEventType()
            .Select(example => new TestCaseData(example.Draft, example.Context)
                .SetArgDisplayNames(example.Draft.EventType));

    [TestCaseSource(nameof(ValidExamples))]
    public void Validate_WhenDraftMatchesCatalog_AcceptsIt(AuditEventDraft draft, AuditContext context)
    {
        Assert.That(_validator.Validate(draft, context), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public void Validate_WhenFailedLoginIsAnonymousWithReason_AcceptsIt()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthLogin,
            AuditOutcome.Failure,
            Details: Details(("reasonCode", "invalid_credentials")));

        Assert.That(_validator.Validate(draft, AnonymousContext), Is.EqualTo(AuditRejectionCode.None));
    }

    // Event type

    [TestCase("auth.unknown")]
    [TestCase("AUTH.LOGIN")]
    [TestCase("auth.login ")]
    [TestCase("")]
    public void Validate_WhenEventTypeIsNotInCatalog_RejectsUnknownEventType(string eventType)
    {
        var draft = new AuditEventDraft(eventType, AuditOutcome.Success);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownEventType));
    }

    // Outcome

    [TestCase(AuditEventTypes.AuthLogout, AuditOutcome.Failure)]
    [TestCase(AuditEventTypes.AuthLogin, AuditOutcome.Denied)]
    [TestCase(AuditEventTypes.AuditEventsViewed, AuditOutcome.Denied)]
    [TestCase(AuditEventTypes.AccessDenied, AuditOutcome.Success)]
    public void Validate_WhenOutcomeIsNotAllowedForType_RejectsOutcome(string eventType, AuditOutcome outcome)
    {
        var draft = new AuditEventDraft(eventType, outcome);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.OutcomeNotAllowed));
    }

    [Test]
    public void Validate_WhenOutcomeIsUndefinedEnumValue_RejectsOutcome()
    {
        var draft = StatusChange((AuditOutcome)99);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.OutcomeNotAllowed));
    }

    // Actor and correlation

    [Test]
    public void Validate_WhenUserActorHasNoId_RejectsContext()
    {
        var context = new AuditContext(new AuditActor(AuditActorKind.User, null), CorrelationId);

        Assert.That(_validator.Validate(StatusChange(), context), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenUserActorHasEmptyId_RejectsContext()
    {
        var context = new AuditContext(new AuditActor(AuditActorKind.User, Guid.Empty), CorrelationId);

        Assert.That(_validator.Validate(StatusChange(), context), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenAnonymousActorHasId_RejectsContext()
    {
        var context = new AuditContext(new AuditActor(AuditActorKind.Anonymous, UserId), CorrelationId);
        var draft = new AuditEventDraft(AuditEventTypes.AuthLoginThrottled, AuditOutcome.Denied);

        Assert.That(_validator.Validate(draft, context), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenActorKindIsNotAllowedForOutcome_RejectsContext()
    {
        // A successful status change always has an authenticated actor.
        Assert.That(_validator.Validate(StatusChange(), AnonymousContext), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenExpiryIsNotFromServiceActor_RejectsContext()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.CourseDelegationExpired,
            AuditOutcome.Success,
            CourseId,
            TargetAccount,
            Details(("action", "audit.events.read"), ("delegationId", DelegationId.ToString())));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [TestCase("")]
    [TestCase("corr 0001")]
    [TestCase("corr-0001\nforged line")]
    [TestCase("<script>")]
    public void Validate_WhenCorrelationIdIsMalformed_RejectsContext(string correlationId)
    {
        var context = new AuditContext(AuditActor.ForUser(UserId), correlationId);

        Assert.That(_validator.Validate(StatusChange(), context), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenCorrelationIdIsLongerThan64_RejectsContext()
    {
        var context = new AuditContext(AuditActor.ForUser(UserId), new string('a', 65));

        Assert.That(_validator.Validate(StatusChange(), context), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [Test]
    public void Validate_WhenCorrelationIdIsExactly64_AcceptsIt()
    {
        var context = new AuditContext(AuditActor.ForUser(UserId), new string('a', 64));

        Assert.That(_validator.Validate(StatusChange(), context), Is.EqualTo(AuditRejectionCode.None));
    }

    // Course

    [Test]
    public void Validate_WhenCourseIsMissingForCourseEvent_RejectsCourseRequired()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.CourseMemberRoleChanged,
            AuditOutcome.Success,
            Resource: TargetAccount);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.CourseRequired));
    }

    [Test]
    public void Validate_WhenCourseIsEmptyGuid_RejectsCourseRequired()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.CourseMemberRoleChanged,
            AuditOutcome.Success,
            Guid.Empty,
            TargetAccount);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.CourseRequired));
    }

    [Test]
    public void Validate_WhenCourseIsGivenForAccountEvent_RejectsCourseNotAllowed()
    {
        var draft = StatusChange() with { CourseId = CourseId };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.CourseNotAllowed));
    }

    // Resource

    [Test]
    public void Validate_WhenRequiredResourceIsMissing_RejectsResource()
    {
        var draft = StatusChange() with { Resource = null };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [Test]
    public void Validate_WhenResourceHasWrongType_RejectsResource()
    {
        var draft = StatusChange() with { Resource = Course };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [Test]
    public void Validate_WhenResourceIsGivenForLogout_RejectsResource()
    {
        var draft = new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success, Resource: TargetAccount);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [TestCase("")]
    [TestCase("id with spaces")]
    [TestCase("id/../other")]
    public void Validate_WhenResourceIdIsMalformed_RejectsResource(string resourceId)
    {
        var draft = StatusChange() with { Resource = new AuditResourceRef(AuditResourceTypes.Account, resourceId) };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    // Details

    [Test]
    public void Validate_WhenDetailKeyIsNotInSchema_RejectsUnknownKey()
    {
        var draft = StatusChange(details: Details(("oldStatus", "active"), ("comment", "ok")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [Test]
    public void Validate_WhenReasonCodeIsGivenOnSuccess_RejectsUnknownKey()
    {
        var draft = StatusChange(details: Details(("reasonCode", "forbidden")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [TestCase("password")]
    [TestCase("Password")]
    [TestCase("accessToken")]
    [TestCase("sessionId")]
    [TestCase("apiKey")]
    public void Validate_WhenDetailKeyLooksLikeASecret_RejectsForbiddenKey(string key)
    {
        var draft = StatusChange(details: Details((key, "value")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.ForbiddenDetailKey));
    }

    [TestCase("deleted")]
    [TestCase("ACTIVE")]
    [TestCase("active\n")]
    [TestCase("")]
    public void Validate_WhenDetailValueBreaksItsFormat_RejectsValue(string value)
    {
        var draft = StatusChange(details: Details(("newStatus", value)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase("resultCount", "-1")]
    [TestCase("resultCount", "100001")]
    [TestCase("resultCount", "007")]
    [TestCase("filterFrom", "2026-11-01T00:00:00+01:00")]
    [TestCase("filterFrom", "2026-13-01T00:00:00Z")]
    [TestCase("filterOutcome", "unknown")]
    public void Validate_WhenFilterSummaryValueIsInvalid_RejectsValue(string key, string value)
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuditEventsViewed,
            AuditOutcome.Success,
            CourseId,
            Course,
            Details((key, value)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [Test]
    public void Validate_WhenMoreThan20DetailKeys_RejectsTooLarge()
    {
        var details = Enumerable.Range(0, AuditEventCatalog.MaxDetailKeys + 1)
            .ToDictionary(i => $"key{i}", _ => "v");

        Assert.That(
            _validator.Validate(StatusChange(details: details), UserContext),
            Is.EqualTo(AuditRejectionCode.DetailsTooLarge));
    }

    [Test]
    public void Validate_WhenDetailsExceed2048Bytes_RejectsTooLarge()
    {
        var details = Enumerable.Range(0, 10)
            .ToDictionary(i => $"key{i}", _ => new string('x', 300));

        Assert.That(
            _validator.Validate(StatusChange(details: details), UserContext),
            Is.EqualTo(AuditRejectionCode.DetailsTooLarge));
    }
}
