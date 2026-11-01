using Platform.Api.Services.Identity;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

/// <summary>Role is not stored yet because the account model has no role.</summary>
/// <param name="Password">Plain text kept only so tests can log in; it is never persisted.</param>
public sealed record TestUser(UserAccount Account, DevelopmentRole Role, string? Password = null)
{
    public Guid Id => Account.Id;
    public string Email => Account.Email;
    public AccountStatus Status => Account.Status;
}

public static class TestUsers
{
    public static TestUser Create(
        IAccountRepository accounts,
        DevelopmentRole role = DevelopmentRole.Student,
        AccountStatus status = AccountStatus.Active,
        string? password = null,
        IPasswordHasher? hasher = null)
    {
        var user = Build(role, status, password, hasher);
        accounts.Save(user.Account);
        return user;
    }

    public static TestUser Build(
        DevelopmentRole role = DevelopmentRole.Student,
        AccountStatus status = AccountStatus.Active,
        string? password = null,
        IPasswordHasher? hasher = null)
    {
        var id = Guid.NewGuid();
        var email = $"{role.ToString().ToLowerInvariant()}-{id:N}@example.com";
        var account = new UserAccount(id, email, status);
        if (password is not null)
            account.SetPasswordHash((hasher ?? new BCryptPasswordHasher()).Hash(password));

        return new TestUser(account, role, password);
    }
}
