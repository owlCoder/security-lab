using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditPostgres")]
public sealed class PostgresAuditEventStoreTests
{
    private readonly string databaseName = "audit_test_" + Guid.NewGuid().ToString("N");
    private string? adminConnectionString;
    private DbContextOptions<AuditDbContext> options = null!;
    private bool databaseCreated;
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_ADMIN_CONNECTION_STRING to run isolated PostgreSQL tests.");
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        adminConnectionString = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        databaseCreated = true;

        builder.Database = databaseName;
        options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(builder.ConnectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (!databaseCreated)
            return;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Append_PreservesAllCatalogExamplesAcrossConnections()
    {
        foreach (var (draft, context) in ValidPerEventType())
        {
            var record = AuditEvent.Create(Guid.NewGuid(), OccurredAt, draft, context);
            await using (var writeContext = OpenContext())
                Assert.That(await new PostgresAuditEventStore(writeContext).AppendAsync(record),
                    Is.EqualTo(AuditAppendStatus.Appended));

            await using var readContext = OpenContext();
            var restored = await new PostgresAuditEventStore(readContext).FindAsync(record.Id);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored!.HasSameContentAs(record), Is.True);
                Assert.That(restored.OccurredAt, Is.EqualTo(record.OccurredAt));
                Assert.That(restored.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
            }
        }
    }

    [Test]
    public async Task Retry_WithSameContent_ReturnsOriginalWithoutDuplicate()
    {
        var record = NewEvent();
        await using var context = OpenContext();
        var store = new PostgresAuditEventStore(context);
        await store.AppendAsync(record);
        var retry = AuditEvent.Create(record.Id, OccurredAt.AddMinutes(1), StatusChange(), UserContext);

        Assert.That(await store.AppendAsync(retry), Is.EqualTo(AuditAppendStatus.AlreadyPresent));
        Assert.That(await context.Events.CountAsync(row => row.Id == record.Id), Is.EqualTo(1));
        Assert.That((await store.FindAsync(record.Id))!.OccurredAt, Is.EqualTo(OccurredAt));
    }

    [Test]
    public async Task Retry_WithDifferentContent_DoesNotOverwriteOriginal()
    {
        var record = NewEvent();
        var changed = AuditEvent.Create(record.Id, OccurredAt,
            StatusChange(details: Details(("oldStatus", "active"), ("newStatus", "archived"))), UserContext);
        await using var context = OpenContext();
        var store = new PostgresAuditEventStore(context);
        await store.AppendAsync(record);

        Assert.That(await store.AppendAsync(changed), Is.EqualTo(AuditAppendStatus.Conflict));
        Assert.That((await store.FindAsync(record.Id))!.HasSameContentAs(record), Is.True);
    }

    [Test]
    public async Task ConcurrentRetries_InsertExactlyOneEvent()
    {
        var record = NewEvent();
        await using var first = OpenContext();
        await using var second = OpenContext();

        var results = await Task.WhenAll(
            new PostgresAuditEventStore(first).AppendAsync(record),
            new PostgresAuditEventStore(second).AppendAsync(record));

        Assert.That(results, Is.EquivalentTo(new[] { AuditAppendStatus.Appended, AuditAppendStatus.AlreadyPresent }));
        Assert.That(await first.Events.CountAsync(row => row.Id == record.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Rollback_RemovesUncommittedAuditInsert()
    {
        var record = NewEvent();
        await using (var context = OpenContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var store = new PostgresAuditEventStore(context);
            await store.AppendAsync(record);
            Assert.That(await store.AppendAsync(record), Is.EqualTo(AuditAppendStatus.AlreadyPresent));
            await transaction.RollbackAsync();
        }

        await using var readContext = OpenContext();
        Assert.That(await new PostgresAuditEventStore(readContext).FindAsync(record.Id), Is.Null);
    }

    [Test]
    public async Task MigrateAgain_PreservesExistingEvent()
    {
        var record = NewEvent();
        await using (var context = OpenContext())
            await new PostgresAuditEventStore(context).AppendAsync(record);

        await using var nextContext = OpenContext();
        await nextContext.Database.MigrateAsync();

        Assert.That((await new PostgresAuditEventStore(nextContext).FindAsync(record.Id))!
            .HasSameContentAs(record), Is.True);
    }

    [Test]
    public async Task Append_WithForbiddenDetail_RejectsBeforeInsert()
    {
        var record = AuditEvent.Create(Guid.NewGuid(), OccurredAt,
            StatusChange(details: Details(("password", "synthetic-marker"))), UserContext);
        await using var context = OpenContext();
        var store = new PostgresAuditEventStore(context);

        var error = Assert.ThrowsAsync<ArgumentException>(async () => await store.AppendAsync(record));

        Assert.That(error!.Message, Does.Not.Contain("synthetic-marker"));
        Assert.That(await store.FindAsync(record.Id), Is.Null);
    }

    private AuditDbContext OpenContext() => new(options);
    private static AuditEvent NewEvent() => AuditEvent.Create(Guid.NewGuid(), OccurredAt, StatusChange(), UserContext);
}
