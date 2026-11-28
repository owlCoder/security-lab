using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record CredentialRecord(
    Guid AccountId,
    AccountStatus Status,
    string? PasswordHash,
    int FailedLoginCount = 0,
    DateTimeOffset? LockoutEnd = null,
    string? Email = null,
    bool EmailConfirmed = true,
    bool NewSessionNotificationsEnabled = false);
