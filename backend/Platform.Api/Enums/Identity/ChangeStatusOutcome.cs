namespace Platform.Api.Enums.Identity;

public enum ChangeStatusOutcome
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    InvalidStatus,
    InvalidTransition,
    SelfChangeNotAllowed,
    Unavailable
}
