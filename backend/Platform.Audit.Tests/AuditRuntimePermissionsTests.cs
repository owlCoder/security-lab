using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditRuntime")]
public sealed class AuditRuntimePermissionsTests
{
    private string connectionString = null!;
    private AuditEvent record = null!;

    [OneTimeSetUp]
    public async Task InsertSyntheticEvent()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to verify the bootstrapped runtime role.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        record = AuditEvent.Create(Guid.NewGuid(),
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), StatusChange(), UserContext);
        await using var context = OpenContext();
        Assert.That(await new PostgresAuditEventStore(context).AppendAsync(record),
            Is.EqualTo(AuditAppendStatus.Appended));
    }

    [Test]
    public async Task Runtime_UsesUnprivilegedLoginWithoutRoleMemberships()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT current_user = 'securitylab_audit_runtime'
                AND session_user = current_user
                AND NOT (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls OR r.rolinherit)
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'audit' AND nspowner = r.oid)
                AND NOT EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'audit' AND c.relowner = r.oid)
            FROM pg_roles r WHERE r.rolname = current_user
            """, connection);

        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(true));
    }

    [Test]
    public async Task Runtime_CanReadPersistedEventAcrossConnections()
    {
        await using var context = OpenContext();
        var restored = await new PostgresAuditEventStore(context).FindAsync(record.Id);

        Assert.That(restored!.HasSameContentAs(record), Is.True);
        Assert.That(restored.OccurredAt, Is.EqualTo(record.OccurredAt));
    }

    [TestCase("UPDATE audit.audit_events SET outcome = 'Failure' WHERE id = @id")]
    [TestCase("DELETE FROM audit.audit_events WHERE id = @id")]
    [TestCase("TRUNCATE audit.audit_events")]
    [TestCase("ALTER TABLE audit.audit_events ADD COLUMN permission_test_marker integer")]
    [TestCase("CREATE TABLE audit.permission_test_marker (id integer)")]
    [TestCase("SELECT * FROM audit.\"__EFMigrationsHistory\"")]
    public async Task Runtime_RejectsForbiddenSqlWithoutChangingEvent(string sql)
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            // Roll back even if a permission regression unexpectedly allows the statement.
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", record.Id);
            var error = Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            Assert.That(error!.SqlState, Is.EqualTo(PostgresErrorCodes.InsufficientPrivilege));
            await transaction.RollbackAsync();
        }

        await using var context = OpenContext();
        Assert.That((await new PostgresAuditEventStore(context).FindAsync(record.Id))!
            .HasSameContentAs(record), Is.True);
    }

    private AuditDbContext OpenContext() => new(new DbContextOptionsBuilder<AuditDbContext>()
        .UseNpgsql(connectionString).Options);
}
