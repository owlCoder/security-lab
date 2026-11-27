using Npgsql;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

/// <summary>Reads stored rows of <c>audit.audit_events</c> in the test database.</summary>
public static class AuditTestData
{
    public sealed record Row(string Outcome, string ActorKind, Guid? ActorId, string CorrelationId, string Raw);

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
}
