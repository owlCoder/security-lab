namespace Platform.Access.AspNet;

internal static class AccessMetadataRules
{
    private const int MaxResourceTypeLength = 32;

    public static bool IsValidResourceType(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxResourceTypeLength) return false;
        if (!char.IsAsciiLetterLower(value[0])) return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    public static bool TryNormalizeResourceId(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(raw) || raw.Length > 64) return false;
        if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty) return false;

        normalized = guid.ToString("D");
        return true;
    }
}
