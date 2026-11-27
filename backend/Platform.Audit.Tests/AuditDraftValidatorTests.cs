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
    [TestCase("corr-0001\n")]
    [TestCase("corr-0001\r\n")]
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
    [TestCase("account-1\n")]
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

    // Account unlock (IDN-05): each case breaks one rule of the account.unlocked schema.

    [TestCase(AuditOutcome.Denied)]
    [TestCase(AuditOutcome.Failure)]
    public void Validate_WhenLockoutEndIsGivenOnRefusedUnlock_RejectsUnknownKey(AuditOutcome outcome)
    {
        // Nothing was cleared, so a refusal has no lockout end to report.
        var draft = Unlock(outcome, Details(("lockoutEnd", "2026-11-23T10:15:00Z")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [Test]
    public void Validate_WhenReasonCodeIsGivenOnSuccessfulUnlock_RejectsUnknownKey()
    {
        var draft = Unlock(details: Details(("lockoutEnd", "2026-11-23T10:15:00Z"), ("reasonCode", "not_locked")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [Test]
    public void Validate_WhenCourseIsGivenForUnlock_RejectsCourseNotAllowed()
    {
        var draft = Unlock() with { CourseId = CourseId };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.CourseNotAllowed));
    }

    [Test]
    public void Validate_WhenAccountIsMissingForUnlock_RejectsResource()
    {
        var draft = Unlock() with { Resource = null };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [TestCase(AuditResourceTypes.Course)]
    [TestCase(AuditResourceTypes.Delegation)]
    [TestCase(AuditResourceTypes.Revision)]
    public void Validate_WhenUnlockResourceIsNotAnAccount_RejectsResource(string resourceType)
    {
        var draft = Unlock() with { Resource = new AuditResourceRef(resourceType, TargetAccountId.ToString()) };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [TestCase("")]
    [TestCase("not-a-timestamp")]
    [TestCase("2026-11-23T10:15:00")]
    [TestCase("2026-11-23T10:15:00+01:00")]
    [TestCase("2026-13-23T10:15:00Z")]
    [TestCase("2026-11-23T10:15:00.12345678Z")]
    [TestCase("2026-11-23T10:15:00Z\n")]
    public void Validate_WhenLockoutEndIsNotAUtcTimestamp_RejectsValue(string value)
    {
        var draft = Unlock(details: Details(("lockoutEnd", value)));

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

    [TestCase(AuditEventTypes.AuthMfaEnrollmentStarted)]
    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    [TestCase(AuditEventTypes.AuthSessionRevoked)]
    public void Validate_WhenNewMfaOrSessionEventHasReasonCodeOnSuccess_RejectsIt(string eventType)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Success,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "already_enabled")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled, "invalid_code")]
    [TestCase(AuditEventTypes.AuthMfaVerified, "replayed_code")]
    [TestCase(AuditEventTypes.AuthSessionRevoked, "not_found")]
    public void Validate_WhenNewMfaOrSessionEventHasFailureAndReasonCode_AcceptsIt(
        string eventType,
        string reasonCode)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: Details(("reasonCode", reasonCode)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled, false)]
    [TestCase(AuditEventTypes.AuthMfaEnabled, true)]
    [TestCase(AuditEventTypes.AuthMfaVerified, false)]
    [TestCase(AuditEventTypes.AuthMfaVerified, true)]
    public void Validate_WhenMfaFailureHasNoReasonCode_RejectsIt(
        string eventType,
        bool useEmptyDetails)
    {
        IReadOnlyDictionary<string, string>? details = useEmptyDetails ? Details() : null;
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: details);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Validate_WhenMfaVerificationIsDeniedWithoutReasonCode_RejectsIt(bool useEmptyDetails)
    {
        IReadOnlyDictionary<string, string>? details = useEmptyDetails ? Details() : null;
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthMfaVerified,
            AuditOutcome.Denied,
            Resource: TargetAccount,
            Details: details);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    public void Validate_WhenMfaSuccessHasNoReasonCode_AcceptsIt(string eventType)
    {
        var draft = new AuditEventDraft(eventType, AuditOutcome.Success, Resource: TargetAccount);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    public void Validate_WhenMfaSuccessHasReasonCode_RejectsUnknownKey(string eventType)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Success,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "invalid_code")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure)]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Failure)]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Denied)]
    public void Validate_WhenMfaReasonCodeIsOutsideOutcomeList_RejectsValue(
        string eventType,
        AuditOutcome outcome)
    {
        var draft = new AuditEventDraft(
            eventType,
            outcome,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "bogus_code")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Failure, "invalid_code")]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Failure, "replayed_code")]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Failure, "invalid_secret")]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Failure, "session_inactive")]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditOutcome.Denied, "attempt_limit")]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure, "invalid_code")]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure, "no_valid_enrollment")]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure, "already_enabled")]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure, "invalid_enrollment")]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditOutcome.Failure, "session_inactive")]
    public void Validate_WhenMfaReasonCodeIsInOutcomeList_AcceptsIt(
        string eventType,
        AuditOutcome outcome,
        string reasonCode)
    {
        var draft = new AuditEventDraft(
            eventType,
            outcome,
            Resource: TargetAccount,
            Details: Details(("reasonCode", reasonCode)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public void Validate_WhenAttemptLimitIsUsedForMfaFailure_RejectsValue()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthMfaVerified,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "attempt_limit")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [Test]
    public void Validate_WhenInvalidCodeIsUsedForMfaDenial_RejectsValue()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthMfaVerified,
            AuditOutcome.Denied,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "invalid_code")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(AuditOutcome.Success, "user_request")]
    [TestCase(AuditOutcome.Success, "mfa_attempts_exceeded")]
    [TestCase(AuditOutcome.Failure, "user_request")]
    [TestCase(AuditOutcome.Failure, "mfa_attempts_exceeded")]
    public void Validate_WhenRevocationCauseIsInClosedList_AcceptsIt(
        AuditOutcome outcome,
        string revocationCause)
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthSessionRevoked,
            outcome,
            Resource: TargetAccount,
            Details: Details(("revocationCause", revocationCause)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase(AuditOutcome.Success)]
    [TestCase(AuditOutcome.Failure)]
    public void Validate_WhenRevocationCauseIsOutsideClosedList_RejectsValue(AuditOutcome outcome)
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthSessionRevoked,
            outcome,
            Resource: TargetAccount,
            Details: Details(("revocationCause", "not_a_cause")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(AuditEventTypes.AuthLogin)]
    [TestCase(AuditEventTypes.AccountStatusChanged)]
    public void Validate_WhenOtherEventUsesUnlistedReasonCode_AcceptsCode(string eventType)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Failure,
            Resource: eventType == AuditEventTypes.AuthLogin ? null : TargetAccount,
            Details: Details(("reasonCode", "extension_reason_code")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public void Validate_WhenMfaEnrollmentStartedHasFailureAndReasonCode_RejectsOutcome()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthMfaEnrollmentStarted,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "invalid_code")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.OutcomeNotAllowed));
    }

    [TestCase(AuditEventTypes.AuthMfaEnrollmentStarted)]
    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    [TestCase(AuditEventTypes.AuthSessionRevoked)]
    public void Validate_WhenNewMfaOrSessionEventHasAnonymousActorOnSuccess_RejectsIt(string eventType)
    {
        var draft = new AuditEventDraft(eventType, AuditOutcome.Success, Resource: TargetAccount);

        Assert.That(_validator.Validate(draft, AnonymousContext), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }

    [TestCase(AuditEventTypes.AuthMfaEnrollmentStarted, AuditRejectionCode.OutcomeNotAllowed)]
    [TestCase(AuditEventTypes.AuthMfaEnabled, AuditRejectionCode.OutcomeNotAllowed)]
    [TestCase(AuditEventTypes.AuthMfaVerified, AuditRejectionCode.None)]
    [TestCase(AuditEventTypes.AuthSessionRevoked, AuditRejectionCode.OutcomeNotAllowed)]
    public void Validate_WhenMfaEventIsDeniedWithReasonCode_OnlyVerifiedAcceptsIt(
        string eventType,
        AuditRejectionCode expected)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Denied,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "attempt_limit")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(expected));
    }

    [TestCase("code", AuditRejectionCode.UnknownDetailKey)]
    [TestCase("secret", AuditRejectionCode.ForbiddenDetailKey)]
    [TestCase("totp", AuditRejectionCode.UnknownDetailKey)]
    public void Validate_WhenNewMfaEventHasUnknownDetailKey_RejectsIt(
        string key,
        AuditRejectionCode expected)
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthMfaEnabled,
            AuditOutcome.Success,
            Resource: TargetAccount,
            Details: Details((key, "value")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(expected));
    }

    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    [TestCase(AuditEventTypes.AuthSessionRevoked)]
    public void Validate_WhenNewMfaOrSessionEventHasNumericReasonCode_RejectsIt(string eventType)
    {
        var draft = new AuditEventDraft(
            eventType,
            AuditOutcome.Failure,
            Resource: TargetAccount,
            Details: Details(("reasonCode", "123456")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [Test]
    public void Validate_WhenRevokedHandleIsNotUuid_RejectsIt()
    {
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthSessionRevoked,
            AuditOutcome.Success,
            Resource: TargetAccount,
            Details: Details(("revokedHandle", "not-a-uuid")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [TestCase(AuditEventTypes.AuthMfaEnrollmentStarted)]
    [TestCase(AuditEventTypes.AuthMfaEnabled)]
    [TestCase(AuditEventTypes.AuthMfaVerified)]
    [TestCase(AuditEventTypes.AuthSessionRevoked)]
    public void Validate_WhenNewMfaOrSessionEventHasNoResource_RejectsIt(string eventType)
    {
        var draft = new AuditEventDraft(eventType, AuditOutcome.Success);

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }
}
