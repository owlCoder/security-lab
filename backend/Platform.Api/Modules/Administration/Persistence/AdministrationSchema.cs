using Npgsql;

namespace Platform.Api.Modules.Administration.Persistence;

/// <summary>Explicit operator migration; never run implicitly on an HTTP request.</summary>
public static class AdministrationSchema
{
    public static async Task MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize migrations in this producer schema; do not lock another team's tables.
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(80408001)", connection, transaction))
            await guard.ExecuteNonQueryAsync(ct);
        foreach (var name in new[] { "001_administration.sql", "002_delivery_receipts.sql" })
        {
            await using var stream = typeof(AdministrationSchema).Assembly.GetManifestResourceStream(
                "Platform.Api.Modules.Administration.Persistence.Migrations." + name)
                ?? throw new InvalidOperationException("An ADM-08 migration resource is missing.");
            using var reader = new StreamReader(stream);
            await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(ct), connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>Operator only: the LOGIN role/secret must already exist outside source control.</summary>
    public static async Task GrantRuntimeRoleAsync(string connectionString, string role, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var stream = typeof(AdministrationSchema).Assembly.GetManifestResourceStream(
            "Platform.Api.Modules.Administration.Persistence.runtime-permissions.sql")
            ?? throw new InvalidOperationException("The ADM-08 runtime permissions resource is missing.");
        using var reader = new StreamReader(stream);
        using var identifiers = new NpgsqlCommandBuilder();
        var sql = (await reader.ReadToEndAsync(ct)).Replace("__RUNTIME_ROLE__", identifiers.QuoteIdentifier(role), StringComparison.Ordinal);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
