using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using static Platform.Audit.Tests.AuditDraftExamples;

// IDN-11 TIM 09

namespace Platform.Audit.Tests;

// IDN-11 (AUD-01, AUD-03): each case breaks one rule of the session.rotated schema.
[TestFixture]
public sealed class SessionRotatedValidationTests
{
    private readonly AuditDraftValidator _validator = new();

    [TestCase("login")]
    [TestCase("mfa_completed")]
    [TestCase("reauth_for_sensitive_change")]
    [TestCase("role_changed")]
    public void Validate_WhenSuccessfulRotationHasTrigger_AcceptsIt(string trigger)
    {
        var draft = Rotation(details: Details(("trigger", trigger)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [TestCase(AuditOutcome.Denied, "session_revoked")]
    [TestCase(AuditOutcome.Failure, "audit_failed")]
    public void Validate_WhenRefusedRotationHasReasonCode_AcceptsIt(AuditOutcome outcome, string reasonCode)
    {
        var draft = Rotation(outcome, Details(("trigger", "login"), ("reasonCode", reasonCode)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public void Validate_WhenReasonCodeIsGivenOnSuccessfulRotation_RejectsUnknownKey()
    {
        var draft = Rotation(details: Details(("trigger", "login"), ("reasonCode", "session_revoked")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [Test]
    public void Validate_WhenRotationHasUnknownDetail_RejectsUnknownKey()
    {
        var draft = Rotation(details: Details(("trigger", "login"), ("chainId", "c1")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.UnknownDetailKey));
    }

    [TestCase("token")]
    [TestCase("sessionToken")]
    [TestCase("sessionHandle")]
    public void Validate_WhenRotationCarriesASecretKey_RejectsForbiddenKey(string key)
    {
        var draft = Rotation(details: Details(("trigger", "login"), (key, "c2Vzc2lvbi10b2tlbg")));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.ForbiddenDetailKey));
    }

    [TestCase("Login")]
    [TestCase("reauthenticated_for_sensitive_change")]
    [TestCase("")]
    public void Validate_WhenTriggerBreaksCodeFormat_RejectsValue(string trigger)
    {
        var draft = Rotation(details: Details(("trigger", trigger)));

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidDetailValue));
    }

    [Test]
    public void Validate_WhenRotationHasNoResource_RejectsResource()
    {
        var draft = Rotation() with { Resource = null };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [Test]
    public void Validate_WhenRotationResourceIsNotAnAccount_RejectsResource()
    {
        var draft = Rotation() with { Resource = Course };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.InvalidResource));
    }

    [Test]
    public void Validate_WhenCourseIsGivenForRotation_RejectsCourseNotAllowed()
    {
        var draft = Rotation() with { CourseId = CourseId };

        Assert.That(_validator.Validate(draft, UserContext), Is.EqualTo(AuditRejectionCode.CourseNotAllowed));
    }

    [TestCase(AuditOutcome.Success)]
    [TestCase(AuditOutcome.Denied)]
    [TestCase(AuditOutcome.Failure)]
    public void Validate_WhenRotationActorIsAnonymous_RejectsContext(AuditOutcome outcome)
    {
        var draft = Rotation(outcome, Details(("trigger", "login")));

        Assert.That(_validator.Validate(draft, AnonymousContext), Is.EqualTo(AuditRejectionCode.InvalidContext));
    }
}
