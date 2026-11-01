using Platform.Api.Services.Identity;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("OPS-01")]
public class TestUsersTests
{
    private InMemoryAccountRepository _accounts = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountRepository();
    }

    [Test]
    public void Create_Defaults_ToActiveStudent()
    {
        var user = TestUsers.Create(_accounts);

        Assert.That(user.Role, Is.EqualTo(DevelopmentRole.Student));
        Assert.That(user.Status, Is.EqualTo(AccountStatus.Active));
    }

    [TestCase(DevelopmentRole.Student, AccountStatus.Active)]
    [TestCase(DevelopmentRole.Teacher, AccountStatus.Active)]
    [TestCase(DevelopmentRole.Administrator, AccountStatus.Active)]
    [TestCase(DevelopmentRole.Student, AccountStatus.Blocked)]
    [TestCase(DevelopmentRole.Teacher, AccountStatus.Archived)]
    public void Create_UsesRequestedRoleAndStatus(DevelopmentRole role, AccountStatus status)
    {
        var user = TestUsers.Create(_accounts, role, status);

        Assert.That(user.Role, Is.EqualTo(role));
        Assert.That(user.Status, Is.EqualTo(status));
    }

    [Test]
    public void Create_SavesTheAccountToTheRepository()
    {
        var user = TestUsers.Create(_accounts, status: AccountStatus.Blocked);

        var saved = _accounts.Find(user.Id);

        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Email, Is.EqualTo(user.Email));
        Assert.That(saved.Status, Is.EqualTo(user.Status));
    }

    [Test]
    public void Build_DoesNotSaveTheAccount()
    {
        var user = TestUsers.Build();

        Assert.That(_accounts.Find(user.Id), Is.Null);
    }

    [Test]
    public void Create_ReturnsDifferentUsersEachTime()
    {
        var first = TestUsers.Create(_accounts);
        var second = TestUsers.Create(_accounts);

        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        Assert.That(second.Email, Is.Not.EqualTo(first.Email));
    }

    [Test]
    public void Create_UsesTheReservedExampleDomain()
    {
        var user = TestUsers.Create(_accounts);

        Assert.That(user.Email, Does.EndWith("@example.com"));
    }

    [Test]
    public void Create_WithPassword_StoresAHashThatVerifies()
    {
        const string password = "pass-for-tests-123";
        var user = TestUsers.Create(_accounts, password: password);

        Assert.That(user.Password, Is.EqualTo(password));
        Assert.That(user.Account.PasswordHash, Is.Not.Null.And.Not.EqualTo(password));
        Assert.That(new BCryptPasswordHasher().Verify(password, user.Account.PasswordHash!), Is.True);
    }

    [Test]
    public void Create_WithoutPassword_HasNoPasswordHash()
    {
        var user = TestUsers.Create(_accounts);

        Assert.That(user.Password, Is.Null);
        Assert.That(user.Account.PasswordHash, Is.Null);
    }
}
