using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Persistence;

[TestFixture]
[Category("IDN09")]
public sealed class BackupCodeStoreTests
{
    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await _database.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _database.DisposeAsync();
    }

    private PlatformIdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<
            PlatformIdentityDbContext>()
            .UseNpgsql(
                _database.GetConnectionString(),
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    PlatformIdentityDbContext.SchemaName))
            .Options;

        return new PlatformIdentityDbContext(options);
    }

    private async Task<Guid> CreateUserAsync()
    {
        Guid userId = Guid.NewGuid();

        await using var db = CreateContext();

        db.Accounts.Add(new UserAccount(
            userId,
            $"backup-{userId:N}@example.com"));

        await db.SaveChangesAsync();

        return userId;
    }

    private static string CreateHash()
    {
        var generator = new BackupCodeGenerator();
        string code = generator.GenerateCode();

        bool success = generator.TryComputeHash(
            code, out string hash);

        Assert.That(success, Is.True);
        Assert.That(hash, Has.Length.EqualTo(64));

        return hash;
    }

    [Test]
    public async Task Save_ValidCode_CanBeFound()
    {
        Guid userId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        bool saved = await store.TrySaveAsync(
            userId, hash, now);

        var result = await store.FindAsync(userId, hash);

        Assert.That(saved, Is.True);
        Assert.That(result.Exists, Is.True);
        Assert.That(result.IsUsed, Is.False);
        Assert.That(result.CreatedAt, Is.EqualTo(now).Within(TimeSpan.FromMicroseconds(1)));


    }

    [Test]
    public async Task Save_DuplicateHash_IsRejected()
    {
        Guid userId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        bool first = await store.TrySaveAsync(
            userId, hash, now);

        bool second = await store.TrySaveAsync(
            userId, hash, now);

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
    }

    [Test]
    public async Task Save_NonexistentUser_IsRejected()
    {
        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        bool result = await store.TrySaveAsync(
            Guid.NewGuid(),
            CreateHash(),
            DateTimeOffset.UtcNow);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Save_InvalidHash_IsRejected()
    {
        Guid userId = await CreateUserAsync();

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        bool result = await store.TrySaveAsync(
            userId,
            "invalid",
            DateTimeOffset.UtcNow);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Find_OtherUsersCode_IsNotFound()
    {
        Guid ownerId = await CreateUserAsync();
        Guid otherUserId = await CreateUserAsync();
        string hash = CreateHash();

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        await store.TrySaveAsync(
            ownerId, hash, DateTimeOffset.UtcNow);

        var result = await store.FindAsync(
            otherUserId, hash);

        Assert.That(result.Exists, Is.False);
    }

    [Test]
    public async Task Find_NonexistentCode_IsNotFound()
    {
        Guid userId = await CreateUserAsync();

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        var result = await store.FindAsync(
            userId, CreateHash());

        Assert.That(result.Exists, Is.False);
    }

    [Test]
    public async Task Consume_ValidCode_Succeeds()
    {
        Guid userId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        DateTimeOffset consumedAt = createdAt.AddSeconds(1);

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        await store.TrySaveAsync(userId, hash, createdAt);

        bool consumed = await store.TryConsumeAsync(
            userId, hash, consumedAt);

        var result = await store.FindAsync(userId, hash);

        Assert.That(consumed, Is.True);
        Assert.That(result.IsUsed, Is.True);
        Assert.That(result.UsedAt, Is.EqualTo(consumedAt).Within(TimeSpan.FromMicroseconds(1)));

    }

    [Test]
    public async Task Consume_AlreadyUsedCode_IsRejected()
    {
        Guid userId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        await store.TrySaveAsync(userId, hash, now);

        bool first = await store.TryConsumeAsync(
            userId, hash, now.AddSeconds(1));

        bool second = await store.TryConsumeAsync(
            userId, hash, now.AddSeconds(2));

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
    }

    [Test]
    public async Task Consume_OtherUsersCode_IsRejected()
    {
        Guid ownerId = await CreateUserAsync();
        Guid otherUserId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using var db = CreateContext();
        var store = new BackupCodeStore(db);

        await store.TrySaveAsync(ownerId, hash, now);

        bool consumed = await store.TryConsumeAsync(
            otherUserId, hash, now.AddSeconds(1));

        Assert.That(consumed, Is.False);

        var original = await store.FindAsync(ownerId, hash);
        Assert.That(original.IsUsed, Is.False);
    }

    [Test]
    public async Task Consume_ConcurrentRequests_OnlyOneSucceeds()
    {
        Guid userId = await CreateUserAsync();
        string hash = CreateHash();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (var db = CreateContext())
        {
            var store = new BackupCodeStore(db);
            bool saved = await store.TrySaveAsync(
                userId, hash, now);

            Assert.That(saved, Is.True);
        }

        var attempts = Enumerable.Range(0, 8)
            .Select(async _ =>
            {
                await using var db = CreateContext();
                var store = new BackupCodeStore(db);

                return await store.TryConsumeAsync(
                    userId, hash, now.AddSeconds(1));
            });

        bool[] results = await Task.WhenAll(attempts);

        Assert.That(results.Count(x => x), Is.EqualTo(1));
        Assert.That(results.Count(x => !x), Is.EqualTo(7));

        await using var verificationDb = CreateContext();
        var verificationStore = new BackupCodeStore(
            verificationDb);

        var record = await verificationStore.FindAsync(
            userId, hash);

        Assert.That(record.Exists, Is.True);
        Assert.That(record.IsUsed, Is.True);
    }
}
