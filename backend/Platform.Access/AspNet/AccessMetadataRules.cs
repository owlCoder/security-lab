using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.AspNet;

internal static class AccessMetadataRules
{
    public static bool IsValidResourceType(string? value) => ResourceType.IsValid(value);

    public static bool TryNormalizeResourceId(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(raw) || raw.Length > 64) return false;
        if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty) return false;

        normalized = guid.ToString("D");
        return true;
    }
}
