using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Persistence;

[TestFixture]
public sealed class BackupCodeRedemptionIntegrationTests
{
    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await _database.StartAsync();

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<PlatformIdentityDbContext>();

        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _database.DisposeAsync();
    }

    [Test]
    public void IdentityModule_ResolvesRedemptionService()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IBackupCodeRedemptionService>();

        Assert.That(
            service,
            Is.InstanceOf<BackupCodeRedemptionService>());
    }

    [Test]
    public async Task ConcurrentRedemptions_OnlyOneSucceeds()
    {
        using var provider = CreateProvider();

        Guid userId = Guid.NewGuid();

        var generator = provider
            .GetRequiredService<IBackupCodeGenerator>();

        string backupCode = generator.GenerateCode();

        bool valid = generator.TryComputeHash(
            backupCode, out string codeHash);

        Assert.That(valid, Is.True);

        DateTimeOffset createdAt =
            DateTimeOffset.UtcNow.AddMinutes(-1);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<PlatformIdentityDbContext>();

            db.Accounts.Add(new UserAccount(
                userId,
                $"redemption-{userId:N}@example.com"));

            await db.SaveChangesAsync();

            var store = scope.ServiceProvider
                .GetRequiredService<IBackupCodeStore>();

            bool saved = await store.TrySaveAsync(
                userId,
                codeHash,
                createdAt);

            Assert.That(saved, Is.True);
        }

        var attempts = Enumerable.Range(0, 8)
            .Select(async _ =>
            {
                await using var scope =
                    provider.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<
                        IBackupCodeRedemptionService>();

                return await service.RedeemAsync(
                    userId,
                    backupCode);
            });

        var results = await Task.WhenAll(attempts);

        Assert.That(
            results.Count(result => result.IsSuccessful),
            Is.EqualTo(1));

        Assert.That(
            results.Count(result => !result.IsSuccessful),
            Is.EqualTo(7));

        using var verificationScope = provider.CreateScope();

        var verificationStore = verificationScope.ServiceProvider
            .GetRequiredService<IBackupCodeStore>();

        var record = await verificationStore.FindAsync(
            userId,
            codeHash);

        Assert.That(record.Exists, Is.True);
        Assert.That(record.IsUsed, Is.True);

        var redemptionService = verificationScope.ServiceProvider
            .GetRequiredService<IBackupCodeRedemptionService>();

        var replayResult = await redemptionService.RedeemAsync(
            userId,
            backupCode);

        Assert.That(replayResult.IsSuccessful, Is.False);
    }

    private ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] =
                        _database.GetConnectionString()
                })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddIdentityModule(configuration);

        return services.BuildServiceProvider(
            validateScopes: true);
    }
}
