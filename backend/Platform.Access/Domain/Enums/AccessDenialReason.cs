namespace Platform.Access.Domain.Enums;

public enum AccessDenialReason
{
    None,
    Unauthenticated,
    AccountNotActive,
    UnknownAction,
    NoPolicy,
    PolicyError
}
