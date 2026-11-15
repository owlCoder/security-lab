using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Administration.Persistence;
using Platform.Api.Modules.Kt1;

namespace Platform.Coursework.Tests;

/// <summary>Real PostgreSQL, isolated database per case; identity/ATT doubles are explicit.</summary>
[TestFixture, NonParallelizable]
public sealed class AdministrationPostgresTests
{
    private string connectionString = null!;
    private string administrationConnection = null!;
    private string databaseName = null!;
    private DemoStore accounts = null!;
    private ServiceProvider provider = null!;
    private PersistentAdministrationService service = null!;
    private string? runtimeRole;

    [SetUp]
    public async Task SetUp()
    {
        runtimeRole = null;
        var input = Environment.GetEnvironmentVariable("COURSEWORK_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(input))
        {
            Assert.Ignore("Use scripts/test-coursework-postgres.ps1 for the required isolated PostgreSQL run.");
            return;
        }
        administrationConnection = input;
        databaseName = "cw_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(administrationConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(input) { Database = databaseName, Timeout = 5, CommandTimeout = 5 };
        connectionString = builder.ConnectionString;
        await AdministrationSchema.MigrateAsync(connectionString);
        accounts = new(NullLogger<DemoStore>.Instance);
        await Sql("""
            INSERT INTO coursework_admin.course_settings(course_id, administrative_label)
                VALUES (@course, 'Administrative label A'), (@other, 'Administrative label B');
            INSERT INTO coursework_admin.grants(grant_id, course_id, grantee_id, granted_by, capabilities, is_root)
                VALUES (@root, @course, @actor, @actor, 7, true), (@otherRoot, @other, @otherActor, @otherActor, 7, true)
            """, ("course", DemoStore.Course), ("other", DemoStore.OtherCourse),
            ("root", DemoStore.RootGrant), ("actor", DemoStore.Admin),
            ("otherRoot", DemoStore.OtherRootGrant), ("otherActor", DemoStore.OtherAdmin));
        var services = new ServiceCollection();
        services.AddSingleton<IAccountReader>(accounts);
        services.AddSingleton(new AdministrationDatabase(connectionString));
        foreach (var action in new[] { AdministrationPolicy.Read, AdministrationPolicy.Rename, AdministrationPolicy.Delegate, AdministrationPolicy.Revoke })
            services.AddAccessAction<PersistentAdministrationPolicy>(new(action, ActionScope.Course, "course", true));
        services.AddAccessControl();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        service = NewService(provider.GetRequiredService<IAccessDecision>());
    }

    private PersistentAdministrationService NewService(IAccessDecision decision) => new(connectionString, accounts, decision,
        TimeProvider.System, NullLogger<PersistentAdministrationService>.Instance);

    [TearDown]
    public async Task TearDown()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (databaseName is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(administrationConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        if (runtimeRole is not null)
        {
            await using var role = new NpgsqlCommand($"DROP ROLE IF EXISTS {runtimeRole}", admin);
            await role.ExecuteNonQueryAsync();
        }
    }

    private async Task<object?> Sql(string text, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(text, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    private Task<OperationResult> Run(Guid? actor, string action = AdministrationPolicy.Read, Guid? course = null,
        RenameCourseRequest? rename = null, DelegateRequest? delegation = null, Guid? grantId = null, RevokeRequest? revoke = null)
        => service.Execute(actor, course ?? DemoStore.Course, action, "cw-" + Guid.NewGuid().ToString("N"), rename, delegation, grantId, revoke);

    private async Task<GrantView> Delegate(string[]? caps = null)
    {
        var result = await Run(DemoStore.Admin, AdministrationPolicy.Delegate,
            delegation: new(DemoStore.DelegateUser, caps ?? ["ViewAdministration", "ManageSettings"]));
        Assert.That(result.Status, Is.EqualTo(200));
        return (GrantView)result.Value!;
    }

    [Test]
    public async Task MigrationIsRepeatableAndDoesNotChangeAnotherSchema()
    {
        await Sql("CREATE SCHEMA neighbor; CREATE TABLE neighbor.marker(value text); INSERT INTO neighbor.marker VALUES ('preserve');");
        await AdministrationSchema.MigrateAsync(connectionString);
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.schema_versions"), Is.EqualTo(1L));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants"), Is.EqualTo(2L));
        Assert.That(await Sql("SELECT value FROM neighbor.marker"), Is.EqualTo("preserve"));
    }

    [Test]
    public async Task DelegationRenameAndRevocationSurviveNewConnectionsAndServiceInstances()
    {
        var grant = await Delegate();
        service = NewService(provider.GetRequiredService<IAccessDecision>());
        Assert.That((await Run(DemoStore.DelegateUser)).Status, Is.EqualTo(200));
        var renamed = await Run(DemoStore.DelegateUser, AdministrationPolicy.Rename, rename: new("new label", 1));
        Assert.That(renamed.Status, Is.EqualTo(200));
        Assert.That(await Sql("SELECT administrative_label FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo("new label"));
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Revoke, grantId: grant.Id, revoke: new(1))).Status, Is.EqualTo(200));
        service = NewService(provider.GetRequiredService<IAccessDecision>());
        Assert.That((await Run(DemoStore.DelegateUser)).Status, Is.EqualTo(404));
        Assert.That((await Run(DemoStore.DelegateUser, AdministrationPolicy.Rename, rename: new("forbidden", 2))).Status, Is.EqualTo(404));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome = 'ALLOW'"), Is.EqualTo(4L));
    }

    [Test]
    public async Task CachedGrantCannotReadOrWriteAfterRevocationCommits()
    {
        var grant = await Delegate();
        await using var earlierRequest = await PostgresAdministrationSession.OpenAsync(connectionString, accounts);
        var captured = (await earlierRequest.FindAuthorityAsync(DemoStore.DelegateUser, DemoStore.Course)).Value!;
        await Run(DemoStore.Admin, AdministrationPolicy.Revoke, grantId: grant.Id, revoke: new(1));
        Assert.That((await earlierRequest.ReadAdministrationAsync(captured)).Status, Is.EqualTo(PortStatus.NotFound));
        Assert.That((await earlierRequest.RenameAsync(captured, new("forbidden", 1))).Status, Is.EqualTo(WriteStatus.Denied));
    }

    [Test]
    public async Task SameVersionConcurrentWritesHaveOneWinnerAndOneCommittedChangeEvent()
    {
        await Delegate();
        using var start = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            start.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Run(DemoStore.DelegateUser, AdministrationPolicy.Rename, rename: new("label " + i, 1));
        })));
        Assert.That(results.Select(r => r.Status).Order(), Is.EqualTo(new[] { 200, 409 }));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(2));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE action = 'course.settings.update' AND outcome = 'ALLOW'"), Is.EqualTo(1L));
    }

    [Test]
    public async Task DuplicateActiveDelegationIsRejectedAndRevokedHistoryIsPreserved()
    {
        var first = await Delegate();
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Delegate,
            delegation: new(DemoStore.DelegateUser, ["ViewAdministration"]))).Status, Is.EqualTo(409));
        await Run(DemoStore.Admin, AdministrationPolicy.Revoke, grantId: first.Id, revoke: new(1));
        var second = await Delegate(["ViewAdministration"]);
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants WHERE grantee_id = @id", ("id", DemoStore.DelegateUser)), Is.EqualTo(2L));
    }

    [TestCase(false), TestCase(true)]
    public async Task AuditInsertFailureRollsBackSettingsAndGrantChanges(bool delegation)
    {
        await Sql("""
            CREATE FUNCTION coursework_admin.reject_audit() RETURNS trigger LANGUAGE plpgsql AS
              $$ BEGIN RAISE EXCEPTION 'test injected audit failure'; END $$;
            CREATE TRIGGER reject_audit BEFORE INSERT ON coursework_admin.outbox
              FOR EACH ROW EXECUTE FUNCTION coursework_admin.reject_audit();
            """);
        var result = delegation
            ? await Run(DemoStore.Admin, AdministrationPolicy.Delegate, delegation: new(DemoStore.DelegateUser, ["ViewAdministration"]))
            : await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new("must roll back", 1));
        Assert.That(result.Status, Is.EqualTo(404));
        // A NEW connection verifies the persisted result, not an exception or an EF cache.
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants"), Is.EqualTo(2L));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox"), Is.EqualTo(0L));
    }

    [Test]
    public async Task MissingOrMismatchedProducerAuditCannotCommitAMutation()
    {
        await using (var session = await PostgresAdministrationSession.OpenAsync(connectionString, accounts))
        {
            var authority = (await session.FindAuthorityAsync(DemoStore.Admin, DemoStore.Course)).Value!;
            Assert.That((await session.RenameAsync(authority, new("cannot commit", 1))).Status, Is.EqualTo(WriteStatus.Updated));
            Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await session.WriteAsync(new(DateTimeOffset.UtcNow, "wrong-target", DemoStore.Admin,
                AdministrationPolicy.Rename, DemoStore.OtherCourse, DemoStore.Course, "ALLOW", "ADMIN_CHANGE", "adm08:v1", 1, 2)));
        }
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox"), Is.EqualTo(0L));
    }

    [TestCase(null), TestCase(""), TestCase("  "), TestCase("line\nbreak")]
    public async Task InvalidSettingsNeverChangePersistentState(string? name)
    {
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new(name, 1))).Status, Is.EqualTo(400));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    [Test]
    public async Task LabelIsBoundAsDataIncludingUnicodeAndSqlPunctuation()
    {
        const string label = "Безбедност '; DROP SCHEMA coursework_admin; --";
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new(label, 1))).Status, Is.EqualTo(200));
        Assert.That(await Sql("SELECT administrative_label FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(label));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants"), Is.EqualTo(2L));
    }

    [TestCase("ViewAdministration"), TestCase("ManageDelegations"), TestCase("Teacher"), TestCase("3")]
    public async Task DelegationCapabilityAllowlistCannotBeEscalated(string capability)
    {
        // View-only is valid but cannot rename or redelegate. The other inputs are rejected.
        var response = await Run(DemoStore.Admin, AdministrationPolicy.Delegate,
            delegation: new(DemoStore.DelegateUser, [capability]));
        if (capability != "ViewAdministration")
        {
            Assert.That(response.Status, Is.EqualTo(400));
            return;
        }
        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That((await Run(DemoStore.DelegateUser, AdministrationPolicy.Rename, rename: new("no", 1))).Status, Is.EqualTo(404));
        Assert.That((await Run(DemoStore.DelegateUser, AdministrationPolicy.Delegate,
            delegation: new(DemoStore.Marko, ["ViewAdministration"]))).Status, Is.EqualTo(404));
    }

    [Test]
    public async Task ForeignCourseAndMissingCourseAreIndistinguishable()
    {
        var foreign = await Run(DemoStore.Admin, course: DemoStore.OtherCourse);
        var missing = await Run(DemoStore.Admin, course: Guid.NewGuid());
        Assert.That(foreign, Is.EqualTo(new OperationResult(404)));
        Assert.That(missing, Is.EqualTo(foreign));
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Revoke, grantId: DemoStore.OtherRootGrant, revoke: new(1))).Status, Is.EqualTo(404));
    }

    [Test]
    public async Task RootCannotBeRevokedAndSelfDelegationCannotProvisionAnotherRoot()
    {
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Revoke, grantId: DemoStore.RootGrant, revoke: new(1))).Status, Is.EqualTo(404));
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Delegate,
            delegation: new(DemoStore.Admin, ["ViewAdministration"]))).Status, Is.EqualTo(400));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants WHERE is_root"), Is.EqualTo(2L));
    }

    [Test]
    public async Task AccountBlockAndIdentityOutageDenyExistingAuthority()
    {
        await Delegate();
        accounts.SetAccountActive(DemoStore.DelegateUser, false);
        Assert.That((await Run(DemoStore.DelegateUser)).Status, Is.EqualTo(404));
        accounts.AccountsUnavailable = true;
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new("no", 1))).Status, Is.EqualTo(404));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    [Test]
    public async Task AdministrativeGrantDoesNotGrantTeachingOrStudentAnswerAccess()
    {
        await Delegate();
        await using var snapshot = await PostgresAdministrationSession.OpenAsync(connectionString, accounts);
        Assert.That((await snapshot.FindAuthorityAsync(DemoStore.DelegateUser, DemoStore.Course)).Status, Is.EqualTo(PortStatus.Ok));
        Assert.That(accounts.FindMembership(DemoStore.DelegateUser, DemoStore.Course).Status, Is.EqualTo(PortStatus.NotFound));
        var attempts = new AttemptPolicy(accounts, accounts, accounts);
        var decision = await attempts.EvaluateAsync(new(new(DemoStore.DelegateUser.ToString(), AccountState.Active),
            AttemptPolicy.Read, new("attempt", DemoStore.AnaAttempt.ToString()), "separate-powers"));
        Assert.That(decision, Is.False);
        Assert.That((await Run(DemoStore.Teacher)).Status, Is.EqualTo(404));
    }

    [Test]
    public async Task AuditContainsOnlyMetadataAndAnonymousDenialIsRecorded()
    {
        var marker = "sensitive-administrative-label";
        await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new(marker, 1));
        Assert.That((await Run(null)).Status, Is.EqualTo(401));
        var json = (string)(await Sql("SELECT json_agg(t)::text FROM coursework_admin.outbox t"))!;
        Assert.That(json, Does.Not.Contain(marker).And.Not.Contain("cookie").And.Not.Contain("password").And.Not.Contain("answer"));
        using var rows = JsonDocument.Parse(json);
        var anonymous = rows.RootElement.EnumerateArray().Single(e => e.GetProperty("reason").GetString() == "UNAUTHENTICATED");
        Assert.That(anonymous.GetProperty("actor_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(anonymous.GetProperty("outcome").GetString(), Is.EqualTo("DENY"));
    }

    [Test]
    public async Task SharedCentralDenialNeverMutatesPersistentState()
    {
        service = NewService(new DenyEveryRequest());
        Assert.That((await Run(DemoStore.Admin, AdministrationPolicy.Rename, rename: new("no", 1))).Status, Is.EqualTo(404));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    [Test]
    public async Task RuntimeRoleCanDelegateButCannotProvisionRootsOrEraseAudit()
    {
        runtimeRole = "cw_runtime_" + Guid.NewGuid().ToString("N");
        // Generated hex only. These credentials live only in the disposable test cluster.
        var password = Guid.NewGuid().ToString("N");
        await Sql($"CREATE ROLE {runtimeRole} LOGIN PASSWORD '{password}'");
        await AdministrationSchema.GrantRuntimeRoleAsync(connectionString, runtimeRole);
        var runtime = new NpgsqlConnectionStringBuilder(connectionString) { Username = runtimeRole, Password = password }.ConnectionString;
        var administration = connectionString;
        connectionString = runtime;
        try
        {
            service = NewService(provider.GetRequiredService<IAccessDecision>());
            await Delegate();
            Assert.That((await Run(DemoStore.DelegateUser, AdministrationPolicy.Rename, rename: new("runtime label", 1))).Status, Is.EqualTo(200));
            Assert.ThrowsAsync<PostgresException>(() => Sql("DELETE FROM coursework_admin.outbox"));
            Assert.ThrowsAsync<PostgresException>(() => Sql("UPDATE coursework_admin.grants SET is_root = true"));
            Assert.ThrowsAsync<PostgresException>(() => Sql("UPDATE coursework_admin.grants SET capabilities = 7"));
            Assert.ThrowsAsync<PostgresException>(() => Sql("TRUNCATE coursework_admin.outbox"));
            Assert.ThrowsAsync<PostgresException>(() => Sql("CREATE TABLE coursework_admin.forbidden(id int)"));
        }
        finally { connectionString = administration; }
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome = 'ALLOW'"), Is.EqualTo(2L));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants WHERE is_root"), Is.EqualTo(2L));
    }

    [Test]
    public async Task CancellationPreventsCommitOfChangeAndAudit()
    {
        using var cancellation = new CancellationTokenSource();
        service = NewService(new CancelDuringDecision(cancellation));
        Assert.ThrowsAsync<OperationCanceledException>(() => service.Execute(DemoStore.Admin, DemoStore.Course,
            AdministrationPolicy.Rename, "cancelled-transaction", rename: new("must roll back", 1), ct: cancellation.Token));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox"), Is.EqualTo(0L));
    }

    private sealed class CancelDuringDecision(CancellationTokenSource source) : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            source.Cancel();
            return ValueTask.FromResult(AccessDecision.Allow("test-cancellation", request.CorrelationId));
        }
    }

    private sealed class DenyEveryRequest : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AccessDecision.Deny(AccessDenialReason.PolicyDenied, request.CorrelationId));
    }
}
