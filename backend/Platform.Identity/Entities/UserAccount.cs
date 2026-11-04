using Platform.Identity.Enums;

namespace Platform.Identity.Entities;

public sealed class UserAccount(Guid id, string email, AccountStatus status = AccountStatus.Active)
{
    public Guid Id { get; } = id;
    public string Email { get; } = email;
    public AccountStatus Status { get; set; } = status;
}
