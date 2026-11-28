using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Audit.Composition;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

/// <summary>
/// Audit setup and queries on the shared disposable database. The HTTP audit tests write through a restricted
/// runtime role, including when INSERT is revoked; readers never use a local development database.
/// </summary>
public static class AuditTestData
{
    public sealed record Row(string Outcome, string ActorKind, Guid? ActorId, string CorrelationId, string Raw);

    private const string RuntimeRole = "identity_audit_test_runtime";
    // Stable for this test process: repeated setup must not invalidate an earlier host's connection string.
    private static readonly string RuntimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static async Task<string> CreateRestrictedRuntimeAsync()
    {
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);
        // The password is generated hexadecimal text and the role name is a fixed test-only identifier.
        await ExecuteAsync($"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{RuntimeRole}') THEN
                    CREATE ROLE {RuntimeRole};
                END IF;
            END;
            $$;
            ALTER ROLE {RuntimeRole} WITH LOGIN PASSWORD '{RuntimePassword}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            GRANT USAGE ON SCHEMA audit TO {RuntimeRole};
            GRANT SELECT, INSERT ON audit.audit_events TO {RuntimeRole};
            """);
        return new NpgsqlConnectionStringBuilder(TestDatabaseFixture.ConnectionString)
        {
            Username = RuntimeRole,
            Password = RuntimePassword,
            Pooling = false
        }.ConnectionString;
    }

    public static Task SetInsertAllowedAsync(bool allowed) => ExecuteAsync(allowed
        ? $"GRANT INSERT ON audit.audit_events TO {RuntimeRole}"
        : $"REVOKE INSERT ON audit.audit_events FROM {RuntimeRole}");

    internal static async Task<List<AuditEventRow>> GetByCorrelationAsync(
        WebApplicationFactory<Program> factory, string correlation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.Events.AsNoTracking().Where(row => row.CorrelationId == correlation).ToListAsync();
    }

    internal static async Task<List<AuditEventRow>> GetByActorAsync(
        WebApplicationFactory<Program> factory, Guid actorId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.Events.AsNoTracking().Where(row => row.ActorId == actorId).ToListAsync();
    }

    /// <summary>The rows of one event type for one account; <see cref="Row.Raw"/> is the whole row as JSON.</summary>
    public static async Task<List<Row>> RowsAsync(Guid accountId, string eventType)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT outcome, actor_kind, actor_id, correlation_id, row_to_json(e)::text
            FROM audit.audit_events e
            WHERE event_type = @type AND resource_id = @id
            """, connection);
        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("id", accountId.ToString());
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Row(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
