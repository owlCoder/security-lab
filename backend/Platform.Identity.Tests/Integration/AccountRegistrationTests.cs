using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// IDN-01 against a real PostgreSQL: accounts are created in identity.accounts, the same store that
/// login (IDN-02) and OIDC federation (IDN-08) use. A unique account; an identifier is never reused.
/// </summary>
[TestFixture]
[Category("IDN-01")]
public sealed class AccountRegistrationTests
{
    private const string StrongPassword = "Str0ng-Password!2026";

    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = TestDatabaseFixture.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddIdentityModule(config);   // the same registration as Program.cs
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeServices() => await _provider.DisposeAsync();

    [SetUp]
    public Task ResetDatabase() => TestDatabaseFixture.ResetDatabaseAsync();

    // ---------- Unique account ----------

    [Test]
    public async Task Valid_registration_creates_active_account_with_hashed_password()
    {
        var email = $"Mixed.{Guid.NewGuid():N}@Example.com";

        var result = await RegisterAsync(email, StrongPassword);

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.Created));
        Assert.That(result.AccountId, Is.Not.Null);

        var account = await FindByIdAsync(result.AccountId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(account.Email, Is.EqualTo(email.ToLowerInvariant()));
            Assert.That(account.Status, Is.EqualTo(AccountStatus.Active));
            Assert.That(account.PasswordHash, Is.Not.Null.And.Not.EqualTo(StrongPassword));
        });

        using var scope = _provider.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.That(hasher.Verify(StrongPassword, account.PasswordHash), Is.True);
    }

    [Test]
    public async Task New_account_gets_the_student_role()
    {
        var result = await RegisterAsync(NewEmail(), StrongPassword);

        var account = await FindByIdAsync(result.AccountId!.Value);
        Assert.That(account.Role, Is.EqualTo(AccountRole.Student));
    }

    // ---------- The account works across the platform (one store) ----------

    [Test]
    public async Task Created_account_can_log_in_with_its_password()
    {
        var email = NewEmail();
        var created = await RegisterAsync(email, StrongPassword);

        using var scope = _provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<ICredentialVerifier>();
        var login = await verifier.VerifyAsync(email, StrongPassword);
        var wrong = await verifier.VerifyAsync(email, "Wrong-Password!2026");

        Assert.Multiple(() =>
        {
            Assert.That(login.IsValid, Is.True);
            Assert.That(login.AccountId, Is.EqualTo(created.AccountId));
            Assert.That(login.Status, Is.EqualTo(AccountStatus.Active));
            Assert.That(wrong.IsValid, Is.False);
        });
    }

    [Test]
    public async Task Created_account_can_be_linked_to_an_external_identity()
    {
        var email = NewEmail();
        var created = await RegisterAsync(email, StrongPassword);

        using var scope = _provider.CreateScope();
        var linker = scope.ServiceProvider.GetRequiredService<IExternalIdentityLinker>();
        var link = await linker.ResolveAsync(
            new ExternalIdentity("https://idp.example.test", "subject-1", email, EmailVerified: true));

        Assert.That(link.Outcome, Is.EqualTo(FederatedLoginOutcome.NewlyLinked));
        Assert.That(link.AccountId, Is.EqualTo(created.AccountId));
    }

    // ---------- Invalid input ----------

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-email")]
    [TestCase("a@")]
    [TestCase("@example.com")]
    [TestCase("two@@example.com")]
    public async Task Invalid_email_is_rejected(string email)
    {
        var result = await RegisterAsync(email, StrongPassword);

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.InvalidInput));
        Assert.That(result.AccountId, Is.Null);
    }

    [TestCase("")]
    [TestCase("short1!A")]
    [TestCase("alllowercase-password-123")]
    [TestCase("NoDigitsOrSpecial-Here")]
    [TestCase("NoSpecialCharacter2026")]
    public async Task Weak_password_is_rejected_and_no_account_is_created(string password)
    {
        var email = NewEmail();

        var result = await RegisterAsync(email, password);

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.InvalidInput));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(0));
    }

    [Test]
    public async Task Too_long_password_is_rejected()
    {
        var result = await RegisterAsync(NewEmail(), "Aa1!" + new string('x', 125));

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.InvalidInput));
    }

    // ---------- Identifier is never reused ----------

    [Test]
    public async Task Duplicate_email_is_rejected()
    {
        var email = NewEmail();

        var first = await RegisterAsync(email, StrongPassword);
        var second = await RegisterAsync(email, StrongPassword);

        Assert.That(first.Outcome, Is.EqualTo(RegistrationOutcome.Created));
        Assert.That(second.Outcome, Is.EqualTo(RegistrationOutcome.IdentifierTaken));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(1));
    }

    [Test]
    public async Task Duplicate_email_differing_only_in_case_or_whitespace_is_rejected()
    {
        var email = NewEmail();

        await RegisterAsync(email, StrongPassword);
        var second = await RegisterAsync($"  {email.ToUpperInvariant()}  ", StrongPassword);

        Assert.That(second.Outcome, Is.EqualTo(RegistrationOutcome.IdentifierTaken));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(1));
    }

    [TestCase(AccountStatus.Archived)]
    [TestCase(AccountStatus.Blocked)]
    public async Task Identifier_of_archived_account_cannot_be_reused(AccountStatus status)
    {
        var email = NewEmail();
        var first = await RegisterAsync(email, StrongPassword);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            var account = await db.Accounts.SingleAsync(a => a.Id == first.AccountId);
            account.Status = status;
            await db.SaveChangesAsync();
        }

        var second = await RegisterAsync(email, StrongPassword);

        Assert.That(second.Outcome, Is.EqualTo(RegistrationOutcome.IdentifierTaken));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(1));
    }

    [Test]
    public async Task Concurrent_registrations_of_same_email_create_exactly_one_account()
    {
        var email = NewEmail();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => Task.Run(() => RegisterAsync(email, StrongPassword))));

        Assert.That(results.Count(r => r.Outcome == RegistrationOutcome.Created), Is.EqualTo(1));
        Assert.That(results.Count(r => r.Outcome == RegistrationOutcome.IdentifierTaken), Is.EqualTo(4));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(1));
    }

    // ---------- Sensitive data ----------

    [Test]
    public void Command_ToString_does_not_leak_password()
    {
        var command = new RegisterAccountCommand("someone@example.com", StrongPassword);

        Assert.That(command.ToString(), Does.Not.Contain(StrongPassword));
    }

    // ---------- Helpers ----------

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    // Every call gets its own scope (and DbContext), like a separate HTTP request.
    private async Task<RegisterAccountResult> RegisterAsync(string email, string password)
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountRegistration>();
        return await service.RegisterAsync(new RegisterAccountCommand(email, password));
    }

    private async Task<UserAccount> FindByIdAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<int> CountByEmailAsync(string email)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var normalized = email.Trim().ToLowerInvariant();
        return await db.Accounts.CountAsync(a => a.Email == normalized);
    }
}