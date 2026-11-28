namespace Platform.Api.Models.Identity;

/// <summary>
/// US-32: the administrator picks a reason from this fixed list. The Audit catalog accepts no free text in details,
/// so the reason is a code (lowercase letters, digits and underscore, at most 32 characters). Proposal, to be confirmed.
/// </summary>
public static class MfaResetReasons
{
    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "lost_device",
            "device_replaced",
            "support_request",
            "security_incident",
            "other"
        };

    public static bool IsValid(string? reason) => reason is not null && Allowed.Contains(reason);
}
