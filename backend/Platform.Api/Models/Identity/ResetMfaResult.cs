using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

public sealed record ResetMfaResult(ResetMfaOutcome Outcome, int SessionsRevoked = 0, bool MfaWasEnabled = false);
