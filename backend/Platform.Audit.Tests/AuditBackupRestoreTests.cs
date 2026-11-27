using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditPostgres")]
public sealed class AuditBackupRestoreTests
{
    private string container = null!;
    private string admin = null!;
    private string runtime = null!;
    private readonly List<string> databases = [];

    [SetUp]
    public void RequireIsolatedRunner()
    {
        container = Environment.GetEnvironmentVariable("AUDIT_TEST_CONTAINER_ID")!;
        admin = Environment.GetEnvironmentVariable("AUDIT_TEST_ADMIN_CONNECTION_STRING")!;
        runtime = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING")!;
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(runtime))
            Assert.Ignore("Run scripts/audit/test-postgres.ps1 for isolated backup/restore tests.");
    }

    [TearDown]
    public async Task DropTestDatabases()
    {
        foreach (var database in databases)
            await SqlAsync(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
        databases.Clear();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Restore_FullDatabaseArchive_PreservesAuditAndBusinessStateOrRejectsCorruption(bool corrupt)
    {
        // Arrange: real schemas and the production status-store/transactional-writer path.
        var source = await CreateDatabaseAsync();
        var destination = await CreateDatabaseAsync();
        var statusEventId = Guid.NewGuid();
        await using (var identity = Identity(source))
        {
            await identity.Database.MigrateAsync();
            identity.Accounts.Add(new UserAccount(TargetAccountId, "restore@example.com"));
            identity.Sessions.Add(new Session(Guid.NewGuid(), TargetAccountId, new byte[32], Guid.NewGuid(),
                AuthLevel.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8)));
            await identity.SaveChangesAsync();
        }
        await using (var audit = Audit(source))
        {
            await audit.Database.MigrateAsync();
            foreach (var (draft, context) in ValidPerEventType())
                await new PostgresAuditEventStore(audit).AppendAsync(
                    AuditEvent.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, draft, context));
        }
        await using (var identity = Identity(source))
        {
            await using var work = await new EfAccountStatusStore(identity).BeginAsync();
            var account = await work.FindForUpdateAsync(TargetAccountId);
            Assert.That(await work.ApplyAsync(account!, AccountStatus.Blocked), Is.EqualTo(1));
            var result = await new PostgresTransactionalAuditWriter(TimeProvider.System).WriteAsync(
                StatusChange() with { EventId = statusEventId }, UserContext, work.Transaction);
            Assert.That(result.IsRecorded, Is.True);
            await work.CommitAsync();
        }

        // Role definitions are cluster-wide and are provisioned by the runner, not pg_dump.
        await SqlAsync(Connection(source), """
            REVOKE ALL ON SCHEMA audit FROM PUBLIC;
            GRANT USAGE ON SCHEMA audit TO securitylab_audit_runtime;
            GRANT SELECT, INSERT ON audit.audit_events TO securitylab_audit_runtime;
            """);
        await using var original = Audit(source);
        var expected = (await original.Events.AsNoTracking().ToListAsync()).Select(row => row.ToDomain()).ToList();
        var expectedAuditMigrations = await original.Database.GetAppliedMigrationsAsync();
        await using var originalIdentity = Identity(source);
        var expectedIdentityMigrations = await originalIdentity.Database.GetAppliedMigrationsAsync();
        var expectedSession = await originalIdentity.Sessions.AsNoTracking().SingleAsync();
        var archive = "/tmp/" + source + ".dump";

        // Act: PostgreSQL 17 tools write binary archives inside the disposable container.
        await DockerAsync(0, "pg_dump", "-U", User(), "-d", source, "-Fc", "-f", archive);
        var checksum = await DockerAsync(0, "sha256sum", archive);
        if (corrupt)
            await DockerAsync(0, "truncate", "-s", "16", archive);
        await DockerAsync(corrupt ? 1 : 0, "pg_restore", "-U", User(), "-d", destination,
            "--exit-on-error", "--single-transaction", archive);

        // Assert: a damaged archive is never reported as a usable restore.
        if (corrupt)
        {
            Assert.That(await DockerAsync(0, "sha256sum", archive), Is.Not.EqualTo(checksum));
            Assert.That(await ScalarAsync(Connection(destination), "SELECT to_regclass('audit.audit_events') IS NULL"), Is.EqualTo(true));
            return;
        }

        Assert.That(await DockerAsync(0, "sha256sum", archive), Is.EqualTo(checksum));
        await using var restored = Audit(destination);
        var actual = (await restored.Events.AsNoTracking().ToListAsync()).Select(row => row.ToDomain()).ToList();
        Assert.That(actual.Select(row => row.Id), Is.EquivalentTo(expected.Select(row => row.Id)));
        foreach (var record in expected)
        {
            var copy = actual.Single(row => row.Id == record.Id);
            Assert.That(copy.HasSameContentAs(record), Is.True);
            Assert.That(copy.OccurredAt, Is.EqualTo(record.OccurredAt));
        }
        Assert.That(await restored.Database.GetAppliedMigrationsAsync(), Is.EqualTo(expectedAuditMigrations));
        await using var restoredIdentity = Identity(destination);
        Assert.That(await restoredIdentity.Database.GetAppliedMigrationsAsync(), Is.EqualTo(expectedIdentityMigrations));
        Assert.That((await restoredIdentity.Accounts.SingleAsync()).Status, Is.EqualTo(AccountStatus.Blocked));
        var restoredSession = await restoredIdentity.Sessions.SingleAsync();
        Assert.That(restoredSession.Id, Is.EqualTo(expectedSession.Id));
        Assert.That(restoredSession.RevokedAt, Is.Not.Null.And.EqualTo(expectedSession.RevokedAt));
        var statusEvent = actual.Single(row => row.Id == statusEventId);
        Assert.That(statusEvent.EventType, Is.EqualTo(AuditEventTypes.AccountStatusChanged));
        Assert.That(statusEvent.ResourceId, Is.EqualTo(TargetAccountId.ToString()));
        Assert.That(statusEvent.Details.GetValueOrDefault("newStatus"), Is.EqualTo("blocked"));
        Assert.That(await ScalarAsync(Connection(destination), """
            SELECT tgenabled = 'A' FROM pg_trigger
            WHERE tgrelid = 'audit.audit_events'::regclass AND tgname = 'audit_events_append_only'
            """), Is.EqualTo(true));
        foreach (var sql in new[] { "UPDATE audit.audit_events SET outcome = 'Failure'", "DELETE FROM audit.audit_events" })
            await AssertDeniedAsync(Connection(destination), sql, "55000");
        foreach (var sql in new[] { "UPDATE audit.audit_events SET outcome = 'Failure'", "DELETE FROM audit.audit_events",
            "TRUNCATE audit.audit_events", "DROP TABLE audit.audit_events", "ALTER TABLE audit.audit_events DISABLE TRIGGER ALL" })
            await AssertDeniedAsync(Connection(destination, runtime), sql, "42501");
        await using var runtimeContext = Audit(destination, runtime);
        Assert.That(await runtimeContext.Events.CountAsync(), Is.EqualTo(expected.Count));
        var next = AuditEvent.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, StatusChange(), UserContext);
        Assert.That(await new PostgresAuditEventStore(runtimeContext).AppendAsync(next), Is.EqualTo(AuditAppendStatus.Appended));
        // Reapplying migrations must not replace restored data or the trigger.
        await restored.Database.MigrateAsync();
        Assert.That(await restored.Events.CountAsync(), Is.EqualTo(expected.Count + 1));
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var name = "audit_restore_" + Guid.NewGuid().ToString("N");
        await SqlAsync(admin, $"CREATE DATABASE \"{name}\" TEMPLATE template0");
        databases.Add(name);
        return name;
    }

    private string User() => new NpgsqlConnectionStringBuilder(admin).Username!;
    private string Connection(string database, string? configured = null) =>
        new NpgsqlConnectionStringBuilder(configured ?? admin) { Database = database, Pooling = false }.ConnectionString;
    private AuditDbContext Audit(string database, string? configured = null) => new(
        new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(Connection(database, configured),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")).Options);
    private PlatformIdentityDbContext Identity(string database) => new(
        new DbContextOptionsBuilder<PlatformIdentityDbContext>().UseNpgsql(Connection(database),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "identity")).Options);

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task SqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertDeniedAsync(string connectionString, string sql, string state)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var error = Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        Assert.That(error!.SqlState, Is.EqualTo(state));
        await transaction.RollbackAsync();
    }

    private async Task<string> DockerAsync(int expectedExitCode, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "exec", container }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        await error;
        Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode), $"{arguments[0]} exit status");
        return await output;
    }
}
