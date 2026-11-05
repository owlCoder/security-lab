namespace Platform.LabLocalization.Domain;

/// <summary>Languages a laboratory revision can be shown in (LAB-07).</summary>
public enum LabLanguage
{
    Sr,
    En
}

public static class LabLanguages
{
    /// <summary>Used when no language is requested and when the requested translation does not exist.</summary>
    public const LabLanguage Default = LabLanguage.Sr;

    /// <summary>
    /// Accepts only "sr" and "en" (any letter case); an empty value means the default language.
    /// Enum.TryParse is not used because it also accepts "1", "99" or "Sr, En".
    /// </summary>
    public static bool TryParse(string? raw, out LabLanguage language)
    {
        language = Default;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (string.Equals(raw, "sr", StringComparison.OrdinalIgnoreCase)) { language = LabLanguage.Sr; return true; }
        if (string.Equals(raw, "en", StringComparison.OrdinalIgnoreCase)) { language = LabLanguage.En; return true; }

        return false;
    }
}
