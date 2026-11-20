using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity.Tests;

[TestFixture]
[Category("Integration")]
public sealed class EfPasswordResetTokenStoreIntegrationTests
{
    private static PlatformIdentityDbContext CreateContext()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "KT1_IDN04_TEST_CONNECTION") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore(
                "Izolovana KT1 PostgreSQL baza nije konfigurisana.");
        }

        var connection = new NpgsqlConnectionStringBuilder(
            connectionString);

        if (connection.Host != "127.0.0.1" ||
            connection.Port != 55432 ||
            connection.Database != "kt1_idn04" ||
            connection.Username != "kt1_test")
        {
            Assert.Fail(
                "Testovi smeju koristiti samo izolovanu KT1 bazu.");
        }

        var options =
            new DbContextOptionsBuilder<PlatformIdentityDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new PlatformIdentityDbContext(options);
    }

    private static PasswordResetToken CreateToken(
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        string tokenHash = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        bool created = PasswordResetToken.TryCreate(
            Guid.NewGuid(),
            tokenHash,
            createdAt,
            expiresAt,
            out var token);

        Assert.That(created, Is.True);

        return token;
    }

    [Test]
    public async Task SaveAndFind_TokenPersistsAcrossContexts()
    {
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(
            now.AddMinutes(-1),
            now.AddMinutes(30));

        await using (var db = CreateContext())
        {
            var store = new EfPasswordResetTokenStore(db);

            bool saved = await store.TrySaveAsync(token);

            Assert.That(saved, Is.True);
        }

        await using (var db = CreateContext())
        {
            var store = new EfPasswordResetTokenStore(db);

            var result = await store.FindByHashAsync(
                token.TokenHash);

            Assert.That(
                result,
                Is.TypeOf<PasswordResetTokenLookupResult.Found>());

            var found =
                (PasswordResetTokenLookupResult.Found)result;

            Assert.That(found.Token.Id, Is.EqualTo(token.Id));
            Assert.That(found.Token.UserId, Is.EqualTo(token.UserId));
            Assert.That(found.Token.IsUsed, Is.False);
        }
    }

    
[Test]
public async Task Save_DuplicateTokenHash_IsRejected()
{
    var now = DateTimeOffset.UtcNow;

    var firstToken = CreateToken(
        now.AddMinutes(-1),
        now.AddMinutes(30));

    bool created = PasswordResetToken.TryCreate(
        Guid.NewGuid(),
        firstToken.TokenHash,
        now.AddMinutes(-1),
        now.AddMinutes(30),
        out var secondToken);

    Assert.That(created, Is.True);

    Assert.Multiple(() =>
    {
        Assert.That(
            secondToken.Id,
            Is.Not.EqualTo(firstToken.Id));

        Assert.That(
            secondToken.TokenHash,
            Is.EqualTo(firstToken.TokenHash));
    });

    await using (var db = CreateContext())
    {
        var store = new EfPasswordResetTokenStore(db);

        bool saved = await store.TrySaveAsync(firstToken);

        Assert.That(saved, Is.True);
    }

    await using (var db = CreateContext())
    {
        var store = new EfPasswordResetTokenStore(db);

        bool saved = await store.TrySaveAsync(secondToken);

        Assert.That(saved, Is.False);
    }
}

    [Test]
    public async Task Consume_ExpiredToken_IsRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(
            now.AddMinutes(-30),
            now.AddMinutes(-10));

        await using var db = CreateContext();
        var store = new EfPasswordResetTokenStore(db);

        Assert.That(await store.TrySaveAsync(token), Is.True);

        bool consumed = await store.TryConsumeAsync(
            token.TokenHash,
            now);

        Assert.That(consumed, Is.False);
    }

    [Test]
    public async Task Consume_TokenCanBeUsedOnlyOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(
            now.AddMinutes(-1),
            now.AddMinutes(30));

        await using var db = CreateContext();
        var store = new EfPasswordResetTokenStore(db);

        Assert.That(await store.TrySaveAsync(token), Is.True);

        bool first = await store.TryConsumeAsync(
            token.TokenHash, now);

        bool second = await store.TryConsumeAsync(
            token.TokenHash, now);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
        });
    }

    [Test]
    public async Task Consume_ConcurrentRequests_OnlyOneSucceeds()
    {
        var now = DateTimeOffset.UtcNow;
        var token = CreateToken(
            now.AddMinutes(-1),
            now.AddMinutes(30));

        await using (var setupDb = CreateContext())
        {
            var setupStore = new EfPasswordResetTokenStore(
                setupDb);

            Assert.That(
                await setupStore.TrySaveAsync(token),
                Is.True);
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();

        var firstStore = new EfPasswordResetTokenStore(firstDb);
        var secondStore = new EfPasswordResetTokenStore(secondDb);

        bool[] results = await Task.WhenAll(
            firstStore.TryConsumeAsync(token.TokenHash, now),
            secondStore.TryConsumeAsync(token.TokenHash, now));

        Assert.That(
            results.Count(success => success),
            Is.EqualTo(1));
    }
}
