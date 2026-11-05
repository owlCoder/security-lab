using System.Text.RegularExpressions;

namespace Platform.Access.Domain.ValueObjects;

public static partial class ActionId
{
    [GeneratedRegex(@"\A[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) =>
        value is not null &&
        value.Length <= 64 &&
        Pattern().IsMatch(value);
}
