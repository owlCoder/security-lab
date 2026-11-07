using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

/// <summary>Value rules for <see cref="AuditDetailFormat"/>.</summary>
internal static partial class AuditDetailFormats
{
    private const int MaxCount = 100_000;

    private static readonly string[] AccountStatuses = ["active", "blocked", "archived"];
    private static readonly string[] CourseRoles = ["none", "student", "teacher", "assistant"];
    private static readonly string[] AccessDenialReasons =
        ["unauthenticated", "account_not_active", "unknown_action", "no_policy", "policy_error"];
    private static readonly string[] OutcomeNames = ["success", "denied", "failure"];

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    // Same rule as ActionId in Platform.Access; Audit does not reference that module.
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionIdPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?Z$", RegexOptions.CultureInvariant)]
    private static partial Regex UtcTimestampPattern();

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex CountPattern();

    public static bool IsValid(AuditDetailFormat format, string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > AuditEventCatalog.MaxDetailValueLength
            || value.Any(char.IsControl))
            return false;

        return format switch
        {
            AuditDetailFormat.Code => CodePattern().IsMatch(value),
            AuditDetailFormat.ActionId => ActionIdPattern().IsMatch(value),
            AuditDetailFormat.AccountStatus => AccountStatuses.Contains(value, StringComparer.Ordinal),
            AuditDetailFormat.CourseRole => CourseRoles.Contains(value, StringComparer.Ordinal),
            AuditDetailFormat.AccessDenialReason => AccessDenialReasons.Contains(value, StringComparer.Ordinal),
            AuditDetailFormat.PolicyId => PolicyIdPattern().IsMatch(value),
            AuditDetailFormat.PolicyVersion => PolicyVersionPattern().IsMatch(value),
            AuditDetailFormat.Uuid => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty,
            AuditDetailFormat.OutcomeName => OutcomeNames.Contains(value, StringComparer.Ordinal),
            AuditDetailFormat.UtcTimestamp => IsUtcTimestamp(value),
            AuditDetailFormat.Count => CountPattern().IsMatch(value) && int.Parse(value, CultureInfo.InvariantCulture) <= MaxCount,
            _ => false
        };
    }

    private static bool IsUtcTimestamp(string value) =>
        UtcTimestampPattern().IsMatch(value)
        && DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
}
