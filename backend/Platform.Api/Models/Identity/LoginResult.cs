using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

/// <param name="MfaRequired">IDN-06: the session is <c>MfaPending</c> and still needs the second factor.</param>
/// <param name="MfaEnrollmentRequired">US-19: the role requires MFA and the account has none yet, so it must enroll first.</param>
public sealed record LoginResult(
    LoginOutcome Outcome,
    Guid? UserId = null,
    IssuedSession? Session = null,
    bool MfaRequired = false,
    bool MfaEnrollmentRequired = false);
