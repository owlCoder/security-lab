namespace Platform.Api.Enums.Identity;

public enum UnlockAccountOutcome
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    NotLocked,
    SelfChangeNotAllowed,
    Unavailable
}
