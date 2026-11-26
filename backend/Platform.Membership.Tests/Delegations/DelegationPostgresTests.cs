
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Membership.Tests.Delegations;

// ACC-07-2: delegacije na pravoj PostgreSQL bazi (Testcontainers, kao MembershipPostgresTests).
// Proverava: dodela + audit u istoj transakciji, opoziv, istorija ostaje, istek se beleži jednom.
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class DelegationPostgresTests
{
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly AuditContext Audit = new(AuditActor.ForUser(Teacher), "acc07-postgres-test");
    private static readonly AuditContext ExpiryAudit =
        new(AuditActor.ForService(DelegationAuditEvents.ExpiryServiceId), "acc07-expiry-test");

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("delegation_tests")
        .WithUsername("delegation_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    private TestContextFactory factory = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await database.StartAsync();
        factory = new TestContextFactory(database.GetConnectionString());

        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(database.GetConnectionString());
    }

    [OneTimeTearDown]
    public async Task StopDatabase() => await database.DisposeAsync();

    [Test]
    public async Task Grant_is_stored_with_one_audit_record_and_works_only_until_expiry()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddHours(1));

        var added = await store.AddAsync(delegation, Audit);

        Assert.That(added, Is.True);
        Assert.That(await store.ActiveActionsAsync(delegation.CourseId, delegation.GranteeId, now),
            Does.Contain("lab.read"));
        Assert.That(await store.ActiveActionsAsync(delegation.CourseId, delegation.GranteeId, now.AddHours(2)),
            Is.Empty);
        Assert.That(await AuditCountAsync(AuditEventTypes.CourseDelegationGranted, delegation), Is.EqualTo(1));
    }

    [Test]
    public async Task Without_an_audit_record_the_grant_is_rolled_back()
    {
        var store = new EfDelegationStore(factory, new UnavailableAuditWriter());
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddHours(1));

        var added = await store.AddAsync(delegation, Audit);

        Assert.That(added, Is.False);
        Assert.That(await Store().FindAsync(delegation.CourseId, delegation.Id), Is.Null);
    }

    [Test]
    public async Task Revoke_ends_access_keeps_history_and_cannot_be_repeated()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddDays(1));
        await store.AddAsync(delegation, Audit);

        var first = await store.RevokeAsync(delegation.CourseId, delegation.Id, Teacher, now.AddMinutes(1), Audit);
        var second = await store.RevokeAsync(delegation.CourseId, delegation.Id, Teacher, now.AddMinutes(2), Audit);

        Assert.That(first, Is.EqualTo(RevokeDelegationResult.Revoked));
        Assert.That(second, Is.EqualTo(RevokeDelegationResult.NotActive));
        Assert.That(await store.ActiveActionsAsync(delegation.CourseId, delegation.GranteeId, now.AddMinutes(3)),
            Is.Empty);

        var stored = await store.FindAsync(delegation.CourseId, delegation.Id);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.RevokedBy, Is.EqualTo(Teacher));
        Assert.That(await AuditCountAsync(AuditEventTypes.CourseDelegationRevoked, delegation), Is.EqualTo(1));
    }

    [Test]
    public async Task Delegation_of_another_course_is_not_found()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddDays(1));
        await store.AddAsync(delegation, Audit);

        var result = await store.RevokeAsync(Guid.NewGuid(), delegation.Id, Teacher, now, Audit);

        Assert.That(result, Is.EqualTo(RevokeDelegationResult.NotFound));
    }

    [Test]
    public async Task Two_simultaneous_revokes_succeed_only_once()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddDays(1));
        await store.AddAsync(delegation, Audit);

        var results = await Task.WhenAll(
            store.RevokeAsync(delegation.CourseId, delegation.Id, Teacher, now.AddMinutes(1), Audit).AsTask(),
            store.RevokeAsync(delegation.CourseId, delegation.Id, Teacher, now.AddMinutes(1), Audit).AsTask());

        Assert.That(results.Count(r => r == RevokeDelegationResult.Revoked), Is.EqualTo(1));
        Assert.That(await AuditCountAsync(AuditEventTypes.CourseDelegationRevoked, delegation), Is.EqualTo(1));
    }

    [Test]
    public async Task Expiry_is_recorded_once_with_a_service_actor()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        var delegation = NewDelegation(now, now.AddMinutes(1));
        await store.AddAsync(delegation, Audit);
        var later = now.AddMinutes(5);

        var pending = await store.ExpiredWithoutRecordAsync(later, 1000);
        var first = await store.RecordExpiryAsync(delegation.Id, later, ExpiryAudit);
        var second = await store.RecordExpiryAsync(delegation.Id, later, ExpiryAudit);

        Assert.That(pending.Select(d => d.Id), Does.Contain(delegation.Id));
        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(await AuditCountAsync(AuditEventTypes.CourseDelegationExpired, delegation), Is.EqualTo(1));
        Assert.That((await store.ExpiredWithoutRecordAsync(later, 1000)).Select(d => d.Id),
            Does.Not.Contain(delegation.Id));
    }

    private EfDelegationStore Store() =>
        new(factory, new PostgresTransactionalAuditWriter(TimeProvider.System));

    private static CourseDelegation NewDelegation(DateTimeOffset now, DateTimeOffset expiresAt) =>
        CourseDelegation.Grant(Guid.NewGuid(), Guid.NewGuid(), "lab.read", Teacher, now, expiresAt);

    private async Task<long> AuditCountAsync(string eventType, CourseDelegation delegation)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_events "
            + "WHERE event_type = @type AND course_id = @course AND resource_id = @resource",
            connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("course", delegation.CourseId);
        command.Parameters.AddWithValue("resource", delegation.GranteeId.ToString("D"));

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class UnavailableAuditWriter : ITransactionalAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditWriteResult.Unavailable());
    }

    private sealed class TestContextFactory(string connectionString) : IDbContextFactory<MembershipDbContext>
    {
        public MembershipDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<MembershipDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MembershipDbContext.Schema))
                .Options);
    }
}
