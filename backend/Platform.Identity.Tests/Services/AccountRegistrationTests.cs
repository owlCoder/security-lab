using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Platform.Identity;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class AccountRegistrationTests
{
    private const string StrongPassword = "Str0ng-Password!2026";

    private PostgreSqlContainer _db = null!;
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _db.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = _db.GetConnectionString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddIdentityModule(config);
        services.AddIdentityModule();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>()
            .Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountDbContext>()
            .Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Valid_registration_creates_active_account_with_hashed_password()
    {
        var email = $"Mixed.{Guid.NewGuid():N}@Example.com";

        var result = await RegisterAsync(email, StrongPassword);

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.Created));
        Assert.That(result.AccountId, Is.Not.Null);

        var user = await FindByIdAsync(result.AccountId!.Value);
        Assert.That(user.Email, Is.EqualTo(email.ToLowerInvariant()));
        Assert.That(user.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(user.EmailConfirmed, Is.False);
        Assert.That(user.PasswordHash, Is.Not.Null.And.Not.EqualTo(StrongPassword));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var loginAccount = await db.Accounts.SingleAsync(account => account.Id == user.Id);
        Assert.That(loginAccount.EmailConfirmed, Is.False);
        Assert.That(loginAccount.PasswordHash, Is.Not.Null.And.Not.EqualTo(StrongPassword));
    }

    [Test]
    public async Task Confirmation_token_is_one_time_and_only_its_hash_is_stored()
    {
        var email = NewEmail();
        var registration = await RegisterAsync(email, StrongPassword);
        using var scope = _provider.CreateScope();
        var confirmation = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();

        var grant = await confirmation.IssueAsync(email);

        Assert.That(grant, Is.Not.Null);
        Assert.That(grant!.Email, Is.EqualTo(email));
        Assert.That(grant.Token, Is.Not.Empty);
        Assert.That(await confirmation.ConfirmAsync(grant.Token), Is.True);
        Assert.That(await confirmation.ConfirmAsync(grant.Token), Is.False);
        Assert.That(await confirmation.IssueAsync(email), Is.Null);

        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var account = await db.Accounts.SingleAsync(candidate => candidate.Id == registration.AccountId);
        var storedToken = await db.EmailConfirmationTokens.SingleAsync(token => token.UserId == account.Id);
        Assert.That(account.EmailConfirmed, Is.True);
        Assert.That(storedToken.TokenHash, Is.EqualTo(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(grant.Token))));
        Assert.That(storedToken.TokenHash.Length, Is.EqualTo(32));

        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        Assert.That((await userManager.FindByIdAsync(account.Id.ToString()))!.EmailConfirmed, Is.True);
    }

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
    public async Task Weak_password_is_rejected_and_no_account_is_created(string password)
    {
        var email = NewEmail();

        var result = await RegisterAsync(email, password);

        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.InvalidInput));
        Assert.That(await CountByEmailAsync(email), Is.EqualTo(0));
    }

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

    [Test]
    public async Task Identifier_of_archived_account_cannot_be_reused()
    {
        var email = NewEmail();
        var first = await RegisterAsync(email, StrongPassword);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == first.AccountId);
            user.Status = AccountStatus.Archived;
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

    [Test]
    public void Command_ToString_does_not_leak_password()
    {
        var command = new RegisterAccountCommand("someone@example.com", StrongPassword);

        Assert.That(command.ToString(), Does.Not.Contain(StrongPassword));
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<RegisterAccountResult> RegisterAsync(string email, string password)
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountRegistration>();
        return await service.RegisterAsync(new RegisterAccountCommand(email, password));
    }

    private async Task<AppUser> FindByIdAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        return await db.Users.SingleAsync(u => u.Id == id);
    }

    private async Task<int> CountByEmailAsync(string email)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.CountAsync(u => u.NormalizedEmail == normalized);
    }
}