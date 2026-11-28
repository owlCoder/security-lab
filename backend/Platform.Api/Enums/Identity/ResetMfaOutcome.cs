namespace Platform.Api.Enums.Identity;

/// <summary>US-32: outcome of the ResetMfa use-case (HTTP level). Same shape as UnlockAccountOutcome.</summary>
public enum ResetMfaOutcome
{
    Success,
    Unauthenticated,
    Forbidden,
    InvalidReason,
    NotFound,
    SelfChangeNotAllowed,
    Unavailable
}
