namespace Platform.Audit.Contracts;

/// <summary>
/// Event types of catalog schema version 1. Adding a type also needs its schema
/// and a test; see docs/components/audit/CONTRACTS.md.
/// </summary>
public static class AuditEventTypes
{
    public const string ContractVersion = "0.1";

    public const string AuthLogin = "auth.login";
    public const string AuthLogout = "auth.logout";
    public const string AuthLoginThrottled = "auth.login.throttled";
    public const string AuthMfaBackupCodesGenerated = "auth.mfa.backup_codes.generated";
    public const string AuthMfaBackupCodeRedeemed = "auth.mfa.backup_code.redeemed";
    public const string AuthMfaBackupCodeRejected = "auth.mfa.backup_code.rejected";
    public const string AuthMfaBackupCodesRevoked = "auth.mfa.backup_codes.revoked";
    public const string AccountStatusChanged = "account.status.changed";
    public const string CourseMemberRoleChanged = "course.member.role.changed";
    public const string CourseDelegationGranted = "course.delegation.granted";
    public const string CourseDelegationRevoked = "course.delegation.revoked";
    public const string CourseDelegationExpired = "course.delegation.expired";
    public const string AccessDenied = "access.denied";
    public const string RevisionSubmitted = "revision.submitted";
    public const string RevisionApproved = "revision.approved";
    public const string RevisionReturned = "revision.returned";
    public const string AuditEventsViewed = "audit.events.viewed";
}
