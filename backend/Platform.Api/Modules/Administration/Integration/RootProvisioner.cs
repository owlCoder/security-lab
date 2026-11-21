using Npgsql;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration.Integration;

public sealed record RootProvisionResult(bool Created, Guid GrantId);

/// <summary>Schema-owner operator boundary. Never exposed through HTTP or the runtime role.</summary>
public sealed class RootProvisioner(string connection, ITrustedCourseDirectory courses, IAccountReader accounts)
{
    public async Task<RootProvisionResult> BootstrapAsync(Guid course, Guid account, CancellationToken ct = default)
    {
        if (course == Guid.Empty || account == Guid.Empty) throw new ArgumentException("Confirmed nonempty IDs are required.");
        var catalog = await ValidateAsync(course, account, ct);
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        // Per-course operator lock also serializes competing first-root requests before a settings row exists.
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@course, 80408))", db, tx))
        { guard.Parameters.AddWithValue("course", course.ToString("D")); await guard.ExecuteNonQueryAsync(ct); }
        await using (var settings = new NpgsqlCommand("""
            INSERT INTO coursework_admin.course_settings(course_id, administrative_label)
            VALUES (@course, @label) ON CONFLICT DO NOTHING
            """, db, tx))
        {
            settings.Parameters.AddWithValue("course", course);
            // The label is local ADM-08 metadata, not a rename of the ACC-01 course.
            var label = new string(catalog.Name.Where(c => !char.IsControl(c)).Take(100).ToArray()).Trim();
            if (label.Length == 0) throw new InvalidOperationException("The catalog name cannot initialize administrative metadata.");
            settings.Parameters.AddWithValue("label", label);
            await settings.ExecuteNonQueryAsync(ct);
        }
        await using (var row = new NpgsqlCommand("SELECT course_id FROM coursework_admin.course_settings WHERE course_id=@course FOR UPDATE", db, tx))
        { row.Parameters.AddWithValue("course", course); await row.ExecuteScalarAsync(ct); }
        Guid? existing = null;
        Guid? currentAccount = null;
        await using (var current = new NpgsqlCommand("SELECT grant_id, grantee_id FROM coursework_admin.grants WHERE course_id=@course AND is_root", db, tx))
        {
            current.Parameters.AddWithValue("course", course);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) { existing = reader.GetGuid(0); currentAccount = reader.GetGuid(1); }
            if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Multiple legacy roots require operator review.");
        }
        if (existing is not null && currentAccount != account)
            throw new InvalidOperationException("A different root already exists; bootstrap cannot replace or escalate it.");
        await ValidateAsync(course, account, ct); // fresh provider reads immediately before own commit
        if (existing is not null)
        { await tx.CommitAsync(ct); return new(false, existing.Value); }
        var grant = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO coursework_admin.grants(grant_id,course_id,grantee_id,granted_by,capabilities,is_root)
            VALUES (@grant,@course,@account,@account,7,true);
            INSERT INTO coursework_admin.outbox(event_id,occurred_at,correlation_id,actor_id,action,target_id,course_id,
                outcome,reason,policy_version,version_before,version_after)
            VALUES (@event,clock_timestamp(),@correlation,NULL,'course.root.bootstrap',@grant,@course,
                'ALLOW','OPERATOR_BOOTSTRAP','adm08:operator-v1',0,1);
            """, db, tx);
        insert.Parameters.AddWithValue("grant", grant);
        insert.Parameters.AddWithValue("course", course);
        insert.Parameters.AddWithValue("account", account);
        insert.Parameters.AddWithValue("event", Guid.NewGuid());
        insert.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
        await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, grant);
    }

    private async Task<TrustedCourse> ValidateAsync(Guid course, Guid account, CancellationToken ct)
    {
        var c = await courses.FindAsync(course, ct);
        var a = await accounts.FindAccountAsync(account, ct);
        if (c is not { Status: PortStatus.Ok, Value.Active: true } || c.Value.Id != course ||
            a is not { Status: PortStatus.Ok, Value.IsActive: true } || a.Value.Id != account)
            throw new InvalidOperationException("An existing active catalog course and Identity account are required.");
        return c.Value;
    }
}
