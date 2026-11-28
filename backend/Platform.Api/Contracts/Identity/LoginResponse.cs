namespace Platform.Api.Contracts.Identity;

/// <param name="UserId">The account that logged in.</param>
/// <param name="MfaRequired">
/// IDN-06: true when the session is <c>MfaPending</c>, so the client asks for the TOTP code
/// (<c>POST /api/auth/mfa/verify</c>) before any other request. Sent only after a correct password.
/// </param>
public sealed record LoginResponse(Guid UserId, bool MfaRequired);