namespace Platform.Api.Contracts.Identity;

/// <param name="UserId">The account that logged in.</param>
/// <param name="MfaRequired">
/// IDN-06: true when the session is <c>MfaPending</c>, so the client asks for the TOTP code
/// (<c>POST /api/auth/mfa/verify</c>) before any other request. Sent only after a correct password.
/// </param>
/// <param name="MfaEnrollmentRequired">
/// US-19: true when the role requires MFA and the account has none yet, so the client starts enrollment
/// (<c>POST /api/auth/mfa/enroll/required</c>) instead of asking for a code.
/// </param>
public sealed record LoginResponse(Guid UserId, bool MfaRequired, bool MfaEnrollmentRequired = false);