using System.Text.RegularExpressions;

namespace Platform.AssignmentPrerequisites.Domain;

public static partial class Identifiers
{
    public const int MaxLength = 64;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValidCourseId(string? value) =>
        value is not null && Pattern().IsMatch(value);

    public static bool IsValidPrincipalId(string? value) =>
        value is not null && Pattern().IsMatch(value);
}
