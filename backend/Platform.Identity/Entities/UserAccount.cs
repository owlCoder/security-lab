using Platform.Identity.Enums;

namespace Platform.Identity.Entities;

public sealed class UserAccount(
    Guid id,
    string email,
    AccountStatus status = AccountStatus.Active,
    AccountRole role = AccountRole.Student,
    bool emailConfirmed = true)
{
    public Guid Id { get; } = id;
    public string Email { get; } = email;
    public AccountStatus Status { get; set; } = status;
    public AccountRole Role { get; set; } = role;
    public bool EmailConfirmed { get; private set; } = emailConfirmed;
    public bool NewSessionNotificationsEnabled { get; private set; }

    /// <summary>BCrypt hash. Null means the account cannot log in with a password.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>IDN-05: consecutive failed logins since the last success or lockout.</summary>
    public int FailedLoginCount { get; private set; }

    /// <summary>IDN-05: logins are refused until this moment. Null or past means not locked.</summary>
    public DateTimeOffset? LockoutEnd { get; private set; }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void ConfirmEmail() => EmailConfirmed = true;

    public void SetNewSessionNotificationsEnabled(bool enabled) =>
        NewSessionNotificationsEnabled = enabled;
}
