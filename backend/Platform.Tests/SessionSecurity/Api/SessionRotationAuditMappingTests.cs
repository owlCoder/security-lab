using System.Text.RegularExpressions;
using Platform.Api.Modules.SessionSecurity.Audit;
using Platform.Audit.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class SessionRotationAuditMappingTests
{
    // Same rule as AuditDetailFormat.Code in the Audit catalog (Team 12).
    private static readonly Regex AuditCode = new("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant);

    [TestCase(RotationTrigger.Login, "login")]
    [TestCase(RotationTrigger.MfaCompleted, "mfa_completed")]
    [TestCase(RotationTrigger.ReauthenticatedForSensitiveChange, "reauth_for_sensitive_change")]
    [TestCase(RotationTrigger.RoleChanged, "role_changed")]
    public void TryMapTrigger_WhenTriggerIsKnown_ReturnsSnakeCaseValue(RotationTrigger trigger, string expected)
    {
        var mapped = SessionRotationAuditMapping.TryMapTrigger(trigger, out var value);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.True);
            Assert.That(value, Is.EqualTo(expected));
        });
    }

    [Test]
    public void TryMapTrigger_WhenTriggerIsUndefined_ReturnsFalse()
    {
        var mapped = SessionRotationAuditMapping.TryMapTrigger((RotationTrigger)99, out var value);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void TryMapTrigger_ForEveryTrigger_ReturnsValueAcceptedByAuditCodeFormat()
    {
        foreach (var trigger in Enum.GetValues<RotationTrigger>())
        {
            Assert.That(SessionRotationAuditMapping.TryMapTrigger(trigger, out var value), Is.True, trigger.ToString());
            Assert.That(AuditCode.IsMatch(value!), Is.True, $"{trigger}: {value}");
        }
    }

    [Test]
    public void TryMapOutcome_WhenRotated_ReturnsSuccessWithoutReasonCode()
    {
        var mapped = SessionRotationAuditMapping.TryMapOutcome(
            SessionRotationCodes.Rotated, out var outcome, out var reasonCode);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.True);
            Assert.That(outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(reasonCode, Is.Null);
        });
    }

    [TestCase(SessionRotationCodes.SessionNotFound, AuditOutcome.Denied, "session_not_found")]
    [TestCase(SessionRotationCodes.SessionRevoked, AuditOutcome.Denied, "session_revoked")]
    [TestCase(SessionRotationCodes.SessionExpired, AuditOutcome.Denied, "session_expired")]
    [TestCase(SessionRotationCodes.SubjectMismatch, AuditOutcome.Denied, "subject_mismatch")]
    [TestCase(SessionRotationCodes.RotationConflict, AuditOutcome.Denied, "rotation_conflict")]
    [TestCase(SessionRotationCodes.AuditFailed, AuditOutcome.Failure, "audit_failed")]
    public void TryMapOutcome_WhenRotationIsRefused_ReturnsOutcomeAndReasonCode(
        string code, AuditOutcome expectedOutcome, string expectedReasonCode)
    {
        var mapped = SessionRotationAuditMapping.TryMapOutcome(code, out var outcome, out var reasonCode);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.True);
            Assert.That(outcome, Is.EqualTo(expectedOutcome));
            Assert.That(reasonCode, Is.EqualTo(expectedReasonCode));
            Assert.That(AuditCode.IsMatch(reasonCode!), Is.True);
        });
    }

    [TestCase(SessionRotationCodes.InvalidRequest)]
    [TestCase("rotated")]
    [TestCase("Unknown")]
    [TestCase("")]
    [TestCase(null)]
    public void TryMapOutcome_WhenCodeIsNotMapped_ReturnsFalse(string? code)
    {
        var mapped = SessionRotationAuditMapping.TryMapOutcome(code, out _, out var reasonCode);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.False);
            Assert.That(reasonCode, Is.Null);
        });
    }
}
