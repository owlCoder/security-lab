namespace Platform.Access.Domain.ValueObjects;

public static class ResourceType
{
    public const int MaxLength = 64;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength ||
            value[0] is < 'a' or > 'z')
        {
            return false;
        }

        return value.All(character =>
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character == '-');
    }
}
