using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

/// <summary>Result of an MFA reset. Carries no secret.</summary>
/// <param name="MfaWasEnabled">False when the target had no MFA (still a success, nothing to remove).</param>
public sealed record MfaResetResult(
    MfaResetOutcome Outcome,
    int SessionsRevoked = 0,
    bool MfaWasEnabled = false)
{
    public bool Succeeded => Outcome == MfaResetOutcome.Success;
}
