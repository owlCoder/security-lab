using System.Text.RegularExpressions;

namespace Platform.Assignments.Domain.ValueObjects;

// ASN-06 TIM 15
// Identifier rule for user IDs that cross the module boundary (actor, student).
// Same character set as AuditResourceRef.Id in Platform.Audit, so the audit adapter can pass it on unchanged.
public static partial class PrincipalId
{
    // \z, not $: in .NET "$" also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) =>
        value is not null && Pattern().IsMatch(value);
}
