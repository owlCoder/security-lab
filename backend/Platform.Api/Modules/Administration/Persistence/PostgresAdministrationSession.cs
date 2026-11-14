using System.Data;
using Npgsql;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration.Persistence;

/// <summary>
/// One operation, one connection, one transaction: grant/settings change and producer
/// audit commit together. Never register this mutable session as a singleton.
/// </summary>
public sealed class PostgresAdministrationSession : ICourseAdministrationStore, ISecurityAudit, IDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly IAccountReader accounts;
    private readonly List<PendingChange> pendingChanges = [];
    private bool completed;

    public PostgresAdministrationSession(string connectionString, IAccountReader accounts)
    {
        this.accounts = accounts;
        var options = new NpgsqlConnectionStringBuilder(connectionString);
        options.Timeout = options.Timeout == 0 ? 5 : Math.Min(options.Timeout, 5);
        options.CommandTimeout = options.CommandTimeout == 0 ? 5 : Math.Min(options.CommandTimeout, 5);
        connection = new(options.ConnectionString);
        try
        {
            connection.Open();
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        }
        catch { connection.Dispose(); throw; }
    }

    private NpgsqlCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 5 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    public PortResult<CourseSnapshot> FindCourse(Guid course)
    {
        using var command = Command("SELECT course_id, administrative_label, version FROM coursework_admin.course_settings WHERE course_id = @course", ("course", course));
        using var reader = command.ExecuteReader();
        return reader.Read() ? PortResult<CourseSnapshot>.Found(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)))
            : PortResult<CourseSnapshot>.Missing();
    }

    public PortResult<AdminGrant> FindAuthority(Guid actor, Guid course)
    {
        using var command = Command("""
            SELECT grant_id, course_id, grantee_id, granted_by, capabilities, state, version, is_root
            FROM coursework_admin.grants WHERE course_id = @course AND grantee_id = @actor AND state = 0
            """, ("course", course), ("actor", actor));
        using var reader = command.ExecuteReader();
        return reader.Read() ? PortResult<AdminGrant>.Found(Grant(reader)) : PortResult<AdminGrant>.Missing();
    }

    private static AdminGrant Grant(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetGuid(3), (AdminCapability)reader.GetInt32(4), (GrantState)reader.GetInt32(5), reader.GetInt32(6), reader.GetBoolean(7));

    private bool AccountActive(Guid actor) => accounts.FindAccount(actor) is { Status: PortStatus.Ok, Value: { IsActive: true } account } && account.Id == actor;

    private bool LockAuthority(AdminGrant expected, AdminCapability capability)
    {
        // All mutations for a course take this same lock first. Revocation and a
        // delegate write have a defined order across different connections/processes.
        using (var course = Command("SELECT course_id FROM coursework_admin.course_settings WHERE course_id = @course FOR UPDATE", ("course", expected.CourseId)))
            if (course.ExecuteScalar() is null) return false;
        var current = FindAuthority(expected.GranteeId, expected.CourseId);
        return current.Value == expected && AccountActive(expected.GranteeId) &&
            AdministrationPolicy.Valid(expected, expected.GranteeId, expected.CourseId, capability);
    }

    public PortResult<AdministrationView> ReadAdministration(AdminGrant authority)
    {
        if (!AccountActive(authority.GranteeId) ||
            !AdministrationPolicy.Valid(authority, authority.GranteeId, authority.CourseId, AdminCapability.ViewAdministration))
            return PortResult<AdministrationView>.Missing();
        // Snapshot/authority binding in ONE statement; no grant cache in a cookie.
        using var command = Command("""
            SELECT c.course_id, c.administrative_label, c.version
            FROM coursework_admin.course_settings c JOIN coursework_admin.grants g USING (course_id)
            WHERE g.grant_id = @grant AND g.grantee_id = @actor AND g.course_id = @course
              AND g.version = @version AND g.state = 0 AND g.capabilities = @caps AND g.is_root = @root
              AND g.granted_by = @grantedBy
            """, ("grant", authority.Id), ("actor", authority.GranteeId), ("course", authority.CourseId),
            ("version", authority.Version), ("caps", (int)authority.Capabilities), ("root", authority.IsRoot), ("grantedBy", authority.GrantedBy));
        using var reader = command.ExecuteReader();
        return reader.Read() ? PortResult<AdministrationView>.Found(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), Names(authority.Capabilities)))
            : PortResult<AdministrationView>.Missing();
    }

    public AdminWriteResult Rename(AdminGrant authority, RenameCourseRequest input)
    {
        if (!LockAuthority(authority, AdminCapability.ManageSettings) || string.IsNullOrWhiteSpace(input.DisplayName) ||
            input.DisplayName.Length > 100 || input.DisplayName.Any(char.IsControl) || input.ExpectedVersion < 1)
            return new(WriteStatus.Denied);
        using var command = Command("""
            UPDATE coursework_admin.course_settings SET administrative_label = @name, version = version + 1
            WHERE course_id = @course AND version = @version
            RETURNING course_id, administrative_label, version
            """, ("name", input.DisplayName.Trim()), ("course", authority.CourseId), ("version", input.ExpectedVersion));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Changed(authority, AdministrationPolicy.Rename, authority.CourseId, new(WriteStatus.Updated,
            new AdministrationView(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), Names(authority.Capabilities)),
            input.ExpectedVersion, input.ExpectedVersion + 1)) : new(WriteStatus.Conflict);
    }

    public AdminWriteResult Delegate(AdminGrant authority, Guid grantee, AdminCapability capabilities)
    {
        if (!LockAuthority(authority, AdminCapability.ManageDelegations) || grantee == Guid.Empty || grantee == authority.GranteeId ||
            capabilities is not (AdminCapability.ViewAdministration or (AdminCapability.ViewAdministration | AdminCapability.ManageSettings)) ||
            !AccountActive(grantee)) return new(WriteStatus.Denied);
        var id = Guid.NewGuid();
        using var command = Command("""
            INSERT INTO coursework_admin.grants(grant_id, course_id, grantee_id, granted_by, capabilities)
            VALUES (@id, @course, @grantee, @actor, @caps) ON CONFLICT DO NOTHING
            """, ("id", id), ("course", authority.CourseId), ("grantee", grantee),
            ("actor", authority.GranteeId), ("caps", (int)capabilities));
        return command.ExecuteNonQuery() == 1 ? Changed(authority, AdministrationPolicy.Delegate, id, new(WriteStatus.Updated,
            new GrantView(id, authority.CourseId, grantee, Names(capabilities), nameof(GrantState.Active), 1), 0, 1))
            : new(WriteStatus.Conflict);
    }

    public AdminWriteResult Revoke(AdminGrant authority, Guid grantId, int expectedVersion)
    {
        if (!LockAuthority(authority, AdminCapability.ManageDelegations)) return new(WriteStatus.Denied);
        AdminGrant target;
        using (var find = Command("""
            SELECT grant_id, course_id, grantee_id, granted_by, capabilities, state, version, is_root
            FROM coursework_admin.grants WHERE grant_id = @id AND course_id = @course
            """, ("id", grantId), ("course", authority.CourseId)))
        using (var reader = find.ExecuteReader())
        {
            if (!reader.Read()) return new(WriteStatus.Denied);
            target = Grant(reader);
        }
        if (target.IsRoot || target.State != GrantState.Active) return new(WriteStatus.Denied);
        if (expectedVersion < 1 || target.Version != expectedVersion) return new(WriteStatus.Conflict);
        using var command = Command("UPDATE coursework_admin.grants SET state = 1, version = version + 1 WHERE grant_id = @id", ("id", grantId));
        command.ExecuteNonQuery();
        return Changed(authority, AdministrationPolicy.Revoke, target.Id, new(WriteStatus.Updated,
            new GrantView(target.Id, target.CourseId, target.GranteeId, Names(target.Capabilities),
            nameof(GrantState.Revoked), expectedVersion + 1), expectedVersion, expectedVersion + 1));
    }

    public void Write(SecurityEvent entry)
    {
        var change = entry.Outcome == "ALLOW" && entry.Action != AdministrationPolicy.Read
            ? pendingChanges.SingleOrDefault(p => p == new PendingChange(entry.ActorId, entry.Action, entry.TargetId,
                entry.CourseId, entry.VersionBefore, entry.VersionAfter)) : null;
        if (entry.Outcome == "ALLOW" && entry.Action != AdministrationPolicy.Read && change is null)
            throw new InvalidOperationException("A change event must match an actual mutation in this transaction.");
        // Explicit fields rather than serializing arbitrary objects/HTTP input.
        using var command = Command("""
            INSERT INTO coursework_admin.outbox(event_id, occurred_at, correlation_id, actor_id, action,
                target_id, course_id, outcome, reason, policy_version, version_before, version_after)
            VALUES (@id, @time, @correlation, @actor, @action, @target, @course, @outcome, @reason, @policy, @before, @after)
            """, ("id", Guid.NewGuid()), ("time", entry.Timestamp.ToUniversalTime()),
            ("correlation", entry.CorrelationId), ("actor", (object?)entry.ActorId ?? DBNull.Value),
            ("action", entry.Action), ("target", entry.TargetId), ("course", (object?)entry.CourseId ?? DBNull.Value),
            ("outcome", entry.Outcome), ("reason", entry.Reason), ("policy", entry.PolicyVersion),
            ("before", (object?)entry.VersionBefore ?? DBNull.Value), ("after", (object?)entry.VersionAfter ?? DBNull.Value));
        command.ExecuteNonQuery();
        if (change is not null) pendingChanges.Remove(change);
    }

    private sealed record PendingChange(Guid? Actor, string Action, Guid Target, Guid? Course, int? Before, int? After);
    private AdminWriteResult Changed(AdminGrant actor, string action, Guid target, AdminWriteResult result)
    {
        pendingChanges.Add(new(actor.GranteeId, action, target, actor.CourseId, result.Before, result.After));
        return result;
    }
    public void Commit()
    {
        if (pendingChanges.Count != 0)
            throw new InvalidOperationException("Cannot commit an administrative change without its matching producer audit.");
        transaction.Commit();
        completed = true;
    }
    public void Dispose() { transaction.Dispose(); connection.Dispose(); completed = true; }
    private static string[] Names(AdminCapability caps) => Enum.GetValues<AdminCapability>()
        .Where(c => c != AdminCapability.None && caps.HasFlag(c)).Select(c => c.ToString()).ToArray();
}
