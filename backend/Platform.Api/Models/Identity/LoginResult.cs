using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

/// <param name="MfaRequired">IDN-06: the session is <c>MfaPending</c> and still needs the second factor.</param>
public sealed record LoginResult(
    LoginOutcome Outcome,
    Guid? UserId = null,
    IssuedSession? Session = null,
    bool MfaRequired = false);
