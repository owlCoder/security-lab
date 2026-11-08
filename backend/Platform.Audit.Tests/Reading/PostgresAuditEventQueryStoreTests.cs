using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;
using static Platform.Audit.Tests.Reading.ReadingExamples;

namespace Platform.Audit.Tests.Reading;

/// <summary>
/// The real SQL of the AUD-06 query against PostgreSQL. Skipped (Ignored) unless
/// AUDIT_TEST_ADMIN_CONNECTION_STRING is set, exactly like the existing PostgreSQL tests.
/// </summary>
[TestFixture]
[Category("AuditPostgres")]
[Property("RequirementId", "AUD-06")]
public sealed class PostgresAuditEventQueryStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);
    private readonly string databaseName = "audit_read_test_" + Guid.NewGuid().ToString("N");
    private string? adminConnectionString;
    private DbContextOptions<AuditDbContext> options = null!;
    private bool databaseCreated;
    private readonly List<AuditEvent> courseEvents = [];

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_ADMIN_CONNECTION_STRING to run isolated PostgreSQL tests.");
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        adminConnectionString = builder.ConnectionString;
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        databaseCreated = true;

        builder.Database = databaseName;
        options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(builder.ConnectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;
        await using var context = new AuditDbContext(options);
        await context.Database.MigrateAsync();

        // Five events in the course, two in another course, one event of a different type.
        for (var i = 0; i < 5; i++)
            courseEvents.Add(Event(CourseId, T0.AddMinutes(i)));
        var others = new[] { Event(OtherCourseId, T0.AddMinutes(10)), Event(OtherCourseId, T0.AddMinutes(11)) };
        var denied = AuditEvent.Create(Guid.NewGuid(), T0.AddMinutes(20),
            new AuditEventDraft(AuditEventTypes.AccessDenied, AuditOutcome.Denied, CourseId), UserContext);
        courseEvents.Add(denied);

        var store = new PostgresAuditEventStore(context);
        foreach (var record in courseEvents.Concat(others))
            Assert.That(await store.AppendAsync(record), Is.EqualTo(AuditAppendStatus.Appended));
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (!databaseCreated)
            return;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Query_ReturnsOnlyTheCourse_NewestFirst()
    {
        var rows = await Query(CourseId, new AuditEventFilter(null, null, null, null, null), 50);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(r => r.Id), Is.EquivalentTo(courseEvents.Select(e => e.Id)));
            Assert.That(rows.All(r => r.CourseId == CourseId), Is.True);
            Assert.That(rows.Select(r => r.OccurredAt), Is.Ordered.Descending);
        }
    }

    [Test]
    public async Task Query_FiltersByTypeOutcomeAndTimeRange()
    {
        var byType = await Query(CourseId, new AuditEventFilter(AuditEventTypes.AccessDenied, null, null, null, null), 50);
        var byOutcome = await Query(CourseId, new AuditEventFilter(null, AuditOutcome.Success, null, null, null), 50);
        var byRange = await Query(CourseId, new AuditEventFilter(null, null, T0.AddMinutes(1), T0.AddMinutes(3), null), 50);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(byType, Has.Count.EqualTo(1));
            Assert.That(byOutcome, Has.Count.EqualTo(5));
            Assert.That(byRange, Has.Count.EqualTo(3), "both bounds are inclusive");
        }
    }

    [Test]
    public async Task Query_KeysetPaging_HasNoGapsOrDuplicates()
    {
        var seen = new List<Guid>();
        AuditCursor? after = null;
        for (var page = 0; page < 10; page++)
        {
            var rows = await Query(CourseId, new AuditEventFilter(null, null, null, null, after), 2);
            if (rows.Count == 0)
                break;
            seen.AddRange(rows.Select(r => r.Id));
            after = new AuditCursor(rows[^1].OccurredAt, rows[^1].Id);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen, Is.Unique);
            Assert.That(seen, Is.EquivalentTo(courseEvents.Select(e => e.Id)));
        }
    }

    [Test]
    public async Task Query_InjectionLikeFilterValue_IsTreatedAsData()
    {
        var rows = await Query(CourseId, new AuditEventFilter("x' OR '1'='1", null, null, null, null), 50);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public async Task Query_UnknownCourse_ReturnsNothing()
    {
        var rows = await Query(Guid.NewGuid(), new AuditEventFilter(null, null, null, null, null), 50);

        Assert.That(rows, Is.Empty);
    }

    private async Task<IReadOnlyList<AuditEvent>> Query(Guid course, AuditEventFilter filter, int take)
    {
        await using var context = new AuditDbContext(options);
        return await new PostgresAuditEventQueryStore(context).QueryCourseAsync(course, filter, take);
    }
}
