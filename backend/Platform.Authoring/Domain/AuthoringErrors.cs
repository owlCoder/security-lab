namespace Platform.Authoring.Domain;

/// <summary>A domain rule was violated. The code is stable and machine-readable.</summary>
public sealed class DomainRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class AuthoringErrorCodes
{
    public const string InvalidId = "INVALID_ID";
    public const string InvalidTitle = "INVALID_TITLE";
    public const string InvalidGoal = "INVALID_GOAL";
    public const string InvalidLabType = "INVALID_LAB_TYPE";
    public const string InvalidName = "INVALID_NAME";
    public const string InvalidVersion = "INVALID_VERSION";
    public const string DraftNotEditable = "DRAFT_NOT_EDITABLE";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
}
