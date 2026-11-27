namespace Platform.Api.Enums.Identity;

public enum MfaOutcome
{
    Success,
    InvalidRequest,
    Unauthorized,
    AlreadyEnabled,
    EnrollmentPending,
    InvalidCode,
    Unavailable
}