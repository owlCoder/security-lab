using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

// S-07 against PostgreSQL: course isolation (T07), stable order, page bounds and the SQL course predicate.
// Calls the query service directly, without an endpoint. Synthetic identifiers only.
[TestFixture]
[Category("AuditPostgres")]
public sealed class PostgresCourseAuditQueryTests
{
    private static readonly Guid CourseK = Guid.Parse("4b4b4b4b-0000-0000-0000-000000000001");
    private static readonly Guid CourseL = Guid.Parse("4c4c4c4c-0000-0000-0000-000000000002");
    private static readonly Guid EmptyCourse = Guid.Parse("4d4d4d4d-0000-0000-0000-000000000003");
    private static readonly Guid OtherActor = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SameTime = Now.AddHours(-1);

    private readonly string databaseName = "audit_query_test_" + Guid.NewGuid().ToString("N");
    private string? adminConnectionString;
    private DbContextOptions<AuditDbContext> options = null!;
    private bool databaseCreated;

    // Events of K that share one timestamp; ids chosen so that the uuid order is obvious.
    private readonly Guid[] sameTimeIds =
    [
        Guid.Parse("00000000-0000-0000-0000-00000000000a"),
        Guid.Parse("00000000-0000-0000-0000-00000000000c"),
        Guid.Parse("00000000-0000-0000-0000-00000000000b")
    ];

    private readonly List<AuditEvent> courseKEvents = [];

    [OneTimeSetUp]
    public async Task CreateDatabaseWithEvents()
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
            databaseCreated = true;
        }

        builder.Database = databaseName;
        options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(builder.ConnectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;
        await using var context = OpenContext();
        await context.Database.MigrateAsync();

        // K: 3 events at the same time, 10 more one minute apart, one denied by another actor,
        // one exactly at a range boundary and one outside the default 7 days.
        foreach (var id in sameTimeIds)
            courseKEvents.Add(RoleChange(id, CourseK, SameTime));
        for (var minute = 1; minute <= 10; minute++)
            courseKEvents.Add(RoleChange(Guid.NewGuid(), CourseK, SameTime.AddMinutes(-minute)));
        courseKEvents.Add(RoleChange(Guid.NewGuid(), CourseK, Now.AddDays(-2), AuditOutcome.Denied, OtherActor,
            "corr-denied"));
        courseKEvents.Add(RoleChange(Guid.NewGuid(), CourseK, Now.AddDays(-3)));
        courseKEvents.Add(RoleChange(Guid.NewGuid(), CourseK, Now.AddDays(-20)));

        var store = new PostgresAuditEventStore(context);
        foreach (var record in courseKEvents)
            Assert.That(await store.AppendAsync(record), Is.EqualTo(AuditAppendStatus.Appended));

        // L: same time, same actor and same correlation as K, so only the course separates them.
        for (var i = 0; i < 5; i++)
            await store.AppendAsync(RoleChange(Guid.NewGuid(), CourseL, SameTime));
        await store.AppendAsync(RoleChange(Guid.NewGuid(), CourseL, Now.AddDays(-2), AuditOutcome.Denied,
            OtherActor, "corr-denied"));

        // An event without a course must never appear in a course view.
        await store.AppendAsync(AuditEvent.Create(Guid.NewGuid(), SameTime, StatusChange(), UserContext));
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
    public async Task Query_CourseK_ReturnsOnlyEventsOfK()
    {
        var page = await QueryOk(new CourseAuditQuery(CourseK, PageSize: "100"));

        var expected = courseKEvents.Where(e => e.OccurredAt >= Now.AddDays(-7)).Select(e => e.Id);
        Assert.That(page.Items.Select(item => item.Id), Is.EquivalentTo(expected));
        Assert.That(page.HasNextPage, Is.False);
    }

    [Test]
    public async Task Query_CourseL_DoesNotSeeKAndCountsOnlyL()
    {
        var page = await QueryOk(new CourseAuditQuery(CourseL, PageSize: "100"));

        Assert.That(page.Items, Has.Count.EqualTo(6));
        Assert.That(page.Items.Select(item => item.Id), Has.None.AnyOf(courseKEvents.Select(e => e.Id).ToArray()));
    }

    [Test]
    public async Task Query_WithFiltersMatchingOtherCourse_DoesNotCrossCourse()
    {
        // The actor and correlation ID also exist in L; the result must still be K only.
        var page = await QueryOk(new CourseAuditQuery(CourseK, Outcome: "denied", ActorId: OtherActor.ToString(),
            CorrelationId: "corr-denied"));

        Assert.That(page.Items.Single().Id, Is.EqualTo(courseKEvents[sameTimeIds.Length + 10].Id));
    }

    [Test]
    public async Task Query_CourseWithoutEvents_ReturnsEmptyPage()
    {
        var page = await QueryOk(new CourseAuditQuery(EmptyCourse));

        Assert.That(page.Items, Is.Empty);
        Assert.That(page.HasNextPage, Is.False);
    }

    [Test]
    public async Task Query_WithEqualTimes_SortsByIdDescending()
    {
        var page = await QueryOk(new CourseAuditQuery(CourseK, From: SameTime.ToString("O"),
            To: SameTime.AddTicks(10).ToString("O")));

        // PostgreSQL compares uuid by its bytes, which matches the ordinal order of the text form.
        var expected = sameTimeIds.OrderByDescending(id => id.ToString(), StringComparer.Ordinal);
        Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(expected));
    }

    [Test]
    public async Task Query_SortsByTimeThenIdDescending()
    {
        var page = await QueryOk(new CourseAuditQuery(CourseK, PageSize: "100"));

        var expected = courseKEvents.Where(e => e.OccurredAt >= Now.AddDays(-7))
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id.ToString(), StringComparer.Ordinal)
            .Select(e => e.Id);
        Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(expected));
    }

    [Test]
    public async Task Query_PagesHaveNoGapOrOverlapAndLastPageHasNoNext()
    {
        var all = (await QueryOk(new CourseAuditQuery(CourseK, PageSize: "100"))).Items.Select(i => i.Id).ToList();
        var seen = new List<Guid>();
        CourseAuditPage page;
        var number = 1;
        do
        {
            page = await QueryOk(new CourseAuditQuery(CourseK, Page: number.ToString(), PageSize: "4"));
            Assert.That(page.Items.Count, Is.LessThanOrEqualTo(4));
            seen.AddRange(page.Items.Select(item => item.Id));
            number++;
        } while (page.HasNextPage);

        Assert.That(seen, Is.EqualTo(all));
        Assert.That(all, Has.Count.EqualTo(15));
        // 15 rows in pages of 4: the 4th page has 3 rows and the 5th page is empty.
        Assert.That(number - 1, Is.EqualTo(4));
        Assert.That((await QueryOk(new CourseAuditQuery(CourseK, Page: "5", PageSize: "4"))).Items, Is.Empty);
    }

    [Test]
    public async Task Query_FromIsInclusiveAndToIsExclusive()
    {
        var boundary = Now.AddDays(-3);

        var fromBoundary = await QueryOk(new CourseAuditQuery(CourseK, From: boundary.ToString("O"),
            To: boundary.AddDays(1).ToString("O")));
        var toBoundary = await QueryOk(new CourseAuditQuery(CourseK, From: boundary.AddDays(-1).ToString("O"),
            To: boundary.ToString("O")));

        Assert.That(fromBoundary.Items.Select(i => i.OccurredAt), Has.Member(boundary));
        Assert.That(toBoundary.Items.Select(i => i.OccurredAt), Has.No.Member(boundary));
    }

    [Test]
    public async Task Query_DtoContainsNoDetailsAndKeepsUtc()
    {
        var item = (await QueryOk(new CourseAuditQuery(CourseK, PageSize: "1"))).Items.Single();

        Assert.That(item.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(typeof(CourseAuditEventDto).GetProperties().Select(p => p.Name),
            Has.None.EqualTo("Details").And.None.EqualTo("CourseId").And.None.EqualTo("SchemaVersion"));
    }

    [Test]
    public async Task Sql_AlwaysFiltersByCourseBeforePaging()
    {
        await using var context = OpenContext();
        var normalizer = CourseAuditQueryNormalizerTests.Normalizer();
        normalizer.Normalize(new CourseAuditQuery(CourseK, Page: "2", PageSize: "10"), out var normalized);

        var sql = PostgresCourseAuditEventReader.BuildQuery(context, normalized!).ToQueryString();

        Assert.That(sql, Does.Contain("a.course_id = @"));
        Assert.That(sql, Does.Contain("ORDER BY a.occurred_at DESC, a.id DESC"));
        Assert.That(sql, Does.Contain("LIMIT @").And.Contain("OFFSET @"));
        Assert.That(sql, Does.Not.Contain("details"));
        Assert.That(sql.IndexOf("course_id", StringComparison.Ordinal),
            Is.LessThan(sql.IndexOf("ORDER BY", StringComparison.Ordinal)));
    }

    [Test]
    public void Reader_WithoutCourse_RefusesToRead()
    {
        var reader = new PostgresCourseAuditEventReader(OpenContext());
        var withoutCourse = new NormalizedCourseAuditQuery(Guid.Empty, null, null, null, Now.AddDays(-1), Now,
            null, 1, 10);

        Assert.ThrowsAsync<ArgumentException>(async () => await reader.ReadPageAsync(withoutCourse));
    }

    private async Task<CourseAuditPage> QueryOk(CourseAuditQuery query)
    {
        await using var context = OpenContext();
        var service = new CourseAuditQueryService(CourseAuditQueryNormalizerTests.Normalizer(),
            new PostgresCourseAuditEventReader(context));

        var result = await service.QueryAsync(query);

        Assert.That(result.Status, Is.EqualTo(CourseAuditQueryStatus.Ok), string.Join(";", result.Errors));
        return result.Page!;
    }

    private static AuditEvent RoleChange(Guid id, Guid courseId, DateTimeOffset occurredAt,
        AuditOutcome outcome = AuditOutcome.Success, Guid? actorId = null, string correlationId = CorrelationId) =>
        AuditEvent.Create(id, occurredAt,
            new AuditEventDraft(AuditEventTypes.CourseMemberRoleChanged, outcome, courseId, TargetAccount,
                outcome == AuditOutcome.Success ? Details(("oldRole", "student"), ("newRole", "assistant")) : null),
            new AuditContext(AuditActor.ForUser(actorId ?? UserId), correlationId));

    private AuditDbContext OpenContext() => new(options);
}
