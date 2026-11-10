// IDN-11 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-11: stable outcome codes of a session rotation. The client never sees which rejection
/// code applied; the codes are for the audit record and the tests.
/// </summary>
public static class SessionRotationCodes
{
    public const string Rotated = "Rotated";
    public const string InvalidRequest = "InvalidRequest";
    public const string SessionNotFound = "SessionNotFound";
    public const string SessionRevoked = "SessionRevoked";
    public const string SessionExpired = "SessionExpired";
    public const string SubjectMismatch = "SubjectMismatch";
    public const string RotationConflict = "RotationConflict";
    public const string AuditFailed = "AuditFailed";
}
