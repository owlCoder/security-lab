namespace Platform.Identity.Domain;

public sealed class UserAccount(Guid id, string email, AccountStatus status = AccountStatus.Active)
{
    public Guid Id { get; } = id;
    public string Email { get; } = email;
    public AccountStatus Status { get; set; } = status;

    /// <summary>BCrypt hash. Null means the account cannot log in with a password.</summary>
    public string? PasswordHash { get; private set; }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
