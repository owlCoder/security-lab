
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class PasswordChangeServiceTests
{
    private const string CurrentPassword = "Str0ng-Password!2026";
    private const string NewPassword = "N3w-Secure!Password2026";

    private PostgreSqlContainer _db = default!;
    private ServiceProvider _provider = default!;

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
        services.AddIdentityModule();

        services.AddScoped<
            IPasswordChangeService,
            PasswordChangeService>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();

        var database = scope.ServiceProvider
            .GetRequiredService<AccountDbContext>();

        await database.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Correct_current_password_changes_password()
    {
        var userId = await CreateAccountAsync();

        using var scope = _provider.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IPasswordChangeService>();

        var result = await service.ChangePasswordAsync(
            userId,
            CurrentPassword,
            NewPassword);

        Assert.That(
            result.Outcome,
            Is.EqualTo(PasswordChangeOutcome.Success));

        Assert.That(
            await PasswordIsValidAsync(userId, NewPassword),
            Is.True);

        Assert.That(
            await PasswordIsValidAsync(userId, CurrentPassword),
            Is.False);
    }

    [Test]
    public async Task Wrong_current_password_is_rejected()
    {
        var userId = await CreateAccountAsync();

        using var scope = _provider.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IPasswordChangeService>();

        var result = await service.ChangePasswordAsync(
            userId,
            "Incorrect-Password!2026",
            NewPassword);

        Assert.That(
            result.Outcome,
            Is.EqualTo(PasswordChangeOutcome.InvalidCurrentPassword));

        Assert.That(
            await PasswordIsValidAsync(userId, CurrentPassword),
            Is.True);

        Assert.That(
            await PasswordIsValidAsync(userId, NewPassword),
            Is.False);
    }

    [Test]
    public async Task Weak_new_password_is_rejected()
    {
        var userId = await CreateAccountAsync();

        using var scope = _provider.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IPasswordChangeService>();

        var result = await service.ChangePasswordAsync(
            userId,
            CurrentPassword,
            "short");

        Assert.That(
            result.Outcome,
            Is.EqualTo(PasswordChangeOutcome.InvalidNewPassword));

        Assert.That(
            await PasswordIsValidAsync(userId, CurrentPassword),
            Is.True);
    }

    [Test]
public async Task Empty_user_id_is_rejected()
{
    using var scope = _provider.CreateScope();

    var service = scope.ServiceProvider
        .GetRequiredService<IPasswordChangeService>();

    var result = await service.ChangePasswordAsync(
        Guid.Empty,
        CurrentPassword,
        NewPassword);

    Assert.That(
        result.Outcome,
        Is.EqualTo(PasswordChangeOutcome.NotAuthenticated));
}

[Test]
public async Task Missing_account_is_rejected()
{
    using var scope = _provider.CreateScope();

    var service = scope.ServiceProvider
        .GetRequiredService<IPasswordChangeService>();

    var result = await service.ChangePasswordAsync(
        Guid.NewGuid(),
        CurrentPassword,
        NewPassword);

    Assert.That(
        result.Outcome,
        Is.EqualTo(PasswordChangeOutcome.Failed));
}

[Test]
public async Task Archived_account_cannot_change_password()
{
    var userId = await CreateAccountAsync();

    using (var scope = _provider.CreateScope())
    {
        var db = scope.ServiceProvider
            .GetRequiredService<AccountDbContext>();

        var user = await db.Users.SingleAsync(
            account => account.Id == userId);

        user.Status = AccountStatus.Archived;

        await db.SaveChangesAsync();
    }

    using var testScope = _provider.CreateScope();

    var service = testScope.ServiceProvider
        .GetRequiredService<IPasswordChangeService>();

    var result = await service.ChangePasswordAsync(
        userId,
        CurrentPassword,
        NewPassword);

    Assert.That(
        result.Outcome,
        Is.EqualTo(PasswordChangeOutcome.Failed));

    Assert.That(
        await PasswordIsValidAsync(userId, CurrentPassword),
        Is.True);
}

    private async Task<Guid> CreateAccountAsync()
    {
        using var scope = _provider.CreateScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<UserManager<AppUser>>();

        var email = $"password-test-{Guid.NewGuid():N}@example.com";

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email
        };

        var result = await manager.CreateAsync(
            user,
            CurrentPassword);

        Assert.That(result.Succeeded, Is.True);

        return user.Id;
    }

    private async Task<bool> PasswordIsValidAsync(
        Guid userId,
        string password)
    {
        using var scope = _provider.CreateScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<UserManager<AppUser>>();

        var user = await manager.FindByIdAsync(
            userId.ToString());

        if (user is not { })
            return false;

        return await manager.CheckPasswordAsync(
            user,
            password);
    }
}
