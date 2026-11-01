using Platform.Api.Services.Identity;
using Platform.Identity.Domain;

namespace Platform.Tests.Identity;

/// <summary>Role is not stored yet because the account model has no role.</summary>
public sealed record TestUser(UserAccount Account, DevelopmentRole Role)
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
        AccountStatus status = AccountStatus.Active)
    {
        var user = Build(role, status);
        accounts.Save(user.Account);
        return user;
    }

    public static TestUser Build(
        DevelopmentRole role = DevelopmentRole.Student,
        AccountStatus status = AccountStatus.Active)
    {
        var id = Guid.NewGuid();
        var email = $"{role.ToString().ToLowerInvariant()}-{id:N}@example.com";
        return new TestUser(new UserAccount(id, email, status), role);
    }
}
