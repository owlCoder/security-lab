namespace Platform.LabLocalization.Domain;

/// <summary>A domain rule was violated. The code is stable and machine-readable.</summary>
public sealed class DomainRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class LocalizationErrorCodes
{
    public const string InvalidRevisionId = "INVALID_REVISION_ID";
    public const string InvalidLanguage = "INVALID_LANGUAGE";
    public const string InvalidTitle = "INVALID_TITLE";
    public const string InvalidDescription = "INVALID_DESCRIPTION";
    public const string InvalidInstructions = "INVALID_INSTRUCTIONS";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
}
