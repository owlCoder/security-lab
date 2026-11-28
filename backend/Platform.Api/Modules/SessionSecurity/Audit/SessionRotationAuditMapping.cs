using System.Diagnostics.CodeAnalysis;
using Platform.Audit.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Audit;

/// <summary>
/// IDN-11 / AUD-01: maps a session rotation to the <c>session.rotated</c> schema of the Audit
/// catalog (Team 12). Values are lower snake case codes; a value the catalog does not know is
/// never guessed, so the caller fails closed instead (AUD-10). Nothing here carries a token,
/// a token hash or a chain ID (AUD-03).
/// </summary>
internal static class SessionRotationAuditMapping
{
    public const string TriggerDetail = "trigger";
    public const string ReasonCodeDetail = "reasonCode";

    /// <summary>Maps a trigger to its <c>trigger</c> detail value.</summary>
    public static bool TryMapTrigger(RotationTrigger trigger, [NotNullWhen(true)] out string? value)
    {
        value = trigger switch
        {
            RotationTrigger.Login => "login",
            RotationTrigger.MfaCompleted => "mfa_completed",
            // AuditDetailFormat.Code allows at most 32 characters, so the full name is shortened.
            RotationTrigger.ReauthenticatedForSensitiveChange => "reauth_for_sensitive_change",
            RotationTrigger.RoleChanged => "role_changed",
            _ => null,
        };
        return value is not null;
    }

    /// <summary>
    /// Maps a <see cref="SessionRotationCodes"/> value to the audit outcome and, for a refusal,
    /// its <c>reasonCode</c>. A success has no reason code.
    /// </summary>
    public static bool TryMapOutcome(string? code, out AuditOutcome outcome, out string? reasonCode)
    {
        (bool Mapped, AuditOutcome Outcome, string? ReasonCode) mapping = code switch
        {
            SessionRotationCodes.Rotated => (true, AuditOutcome.Success, null),
            SessionRotationCodes.SessionNotFound => (true, AuditOutcome.Denied, "session_not_found"),
            SessionRotationCodes.SessionRevoked => (true, AuditOutcome.Denied, "session_revoked"),
            SessionRotationCodes.SessionExpired => (true, AuditOutcome.Denied, "session_expired"),
            SessionRotationCodes.SubjectMismatch => (true, AuditOutcome.Denied, "subject_mismatch"),
            SessionRotationCodes.RotationConflict => (true, AuditOutcome.Denied, "rotation_conflict"),
            SessionRotationCodes.AuditFailed => (true, AuditOutcome.Failure, "audit_failed"),
            _ => (false, default, null),
        };

        outcome = mapping.Outcome;
        reasonCode = mapping.ReasonCode;
        return mapping.Mapped;
    }
}
