using System.Text.RegularExpressions;

namespace Platform.Access.Infrastructure.Auditing;

internal static partial class AuditIdentifier
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.:@-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && Pattern().IsMatch(value);
}
