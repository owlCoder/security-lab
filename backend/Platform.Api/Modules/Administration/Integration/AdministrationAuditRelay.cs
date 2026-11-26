using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Administration.Integration;

public sealed record AuditRelayResult(int Acknowledged, int Failed, long RetainedWithoutCatalogMapping);

/// <summary>Explicit operator batch over durable server-authored producer events. No HTTP input.</summary>
public sealed class AdministrationAuditRelay(string connection, ITrustedAuditWriter writer)
{
    public async Task<AuditRelayResult> RunOnceAsync(CancellationToken ct = default)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(ct);
        // A batch lease avoids duplicate concurrent deliveries; stable IDs also survive a crash before receipt.
        await using var tx = await db.BeginTransactionAsync(ct);
        await using (var guard = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(80408003)", db, tx))
            if (await guard.ExecuteScalarAsync(ct) is not true) return new(0, 0, 0);
        const string supported = "(e.outcome='DENY' OR (e.outcome='ALLOW' AND e.action IN ('course.delegation.create','course.delegation.revoke')))";
        var events = new List<Entry>();
        await using (var select = new NpgsqlCommand($"""
            SELECT e.event_id,e.correlation_id,e.actor_id,e.action,e.target_id,e.course_id,e.outcome,
                g.grantee_id,g.capabilities
            FROM coursework_admin.outbox e
            LEFT JOIN coursework_admin.grants g ON g.grant_id=e.target_id AND g.course_id=e.course_id
            LEFT JOIN coursework_admin.outbox_deliveries d ON d.event_id=e.event_id
            WHERE d.event_id IS NULL AND {supported}
            ORDER BY e.occurred_at,e.event_id LIMIT 100
            """, db, tx))
        await using (var reader = await select.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) events.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.IsDBNull(8) ? 0 : reader.GetInt32(8)));
        int acknowledged = 0, failed = 0;
        foreach (var entry in events)
        {
            var drafts = Drafts(entry);
            if (drafts.Count == 0) { failed++; continue; }
            var context = new AuditContext(entry.Actor is { } actor ? AuditActor.ForUser(actor) : AuditActor.Anonymous, entry.Correlation);
            var recorded = true;
            foreach (var draft in drafts)
            {
                var result = await writer.WriteAsync(draft, context, ct);
                if (!result.IsRecorded) { recorded = false; break; }
            }
            if (!recorded) { failed++; continue; }
            await using var receipt = new NpgsqlCommand("INSERT INTO coursework_admin.outbox_deliveries(event_id) VALUES (@id) ON CONFLICT DO NOTHING", db, tx);
            receipt.Parameters.AddWithValue("id", entry.Id);
            await receipt.ExecuteNonQueryAsync(ct);
            acknowledged++;
        }
        await using var retainedQuery = new NpgsqlCommand($"""
            SELECT count(*) FROM coursework_admin.outbox e LEFT JOIN coursework_admin.outbox_deliveries d USING(event_id)
            WHERE d.event_id IS NULL AND NOT {supported}
            """, db, tx);
        var retained = (long)(await retainedQuery.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return new(acknowledged, failed, retained);
    }

    private static List<AuditEventDraft> Drafts(Entry e)
    {
        if (e.Outcome == "DENY") return [new(AuditEventTypes.AccessDenied, AuditOutcome.Denied, e.Course,
            Details: new Dictionary<string,string> { ["action"] = e.Action }, EventId: e.Id)];
        if (e.Actor is null || e.Course is null || e.Grantee is null || e.Capabilities is not (1 or 3)) return [];
        var type = e.Action == AdministrationPolicy.Delegate ? AuditEventTypes.CourseDelegationGranted : AuditEventTypes.CourseDelegationRevoked;
        var actions = e.Capabilities == 3 ? new[] { AdministrationPolicy.Read, AdministrationPolicy.Rename } : [AdministrationPolicy.Read];
        return actions.Select(action => new AuditEventDraft(type, AuditOutcome.Success, e.Course,
            new(AuditResourceTypes.Account, e.Grantee.Value.ToString("D")), new Dictionary<string,string>
            { ["action"] = action, ["delegationId"] = e.Target.ToString("D") }, StableId(e.Id, action))).ToList();
    }
    private static Guid StableId(Guid source, string action) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"adm08:v1:{source:D}:{action}")).AsSpan(0,16));
    private sealed record Entry(Guid Id,string Correlation,Guid? Actor,string Action,Guid Target,Guid? Course,string Outcome,Guid? Grantee,int Capabilities);
}
