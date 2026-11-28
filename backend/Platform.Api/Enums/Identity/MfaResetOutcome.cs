namespace Platform.Api.Enums.Identity;

/// <summary>US-32: outcome of an administrator MFA reset (service level).</summary>
public enum MfaResetOutcome
{
    Success,
    InvalidReason,
    NotAuthorized,
    TargetNotFound,
    SelfResetNotAllowed,

    /// <summary>The MFA store cannot perform the reset right now (also the default until the real service is registered).</summary>
    Unavailable
}
