using System.Text.RegularExpressions;

namespace Platform.Access.Domain.ValueObjects;

public static partial class ActionId
{
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) =>
        value is not null &&
        value.Length <= 64 &&
        Pattern().IsMatch(value);
}
