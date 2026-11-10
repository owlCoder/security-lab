using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Api.Modules.Assignments.Persistence;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Tests.AssignmentsAuditApi;

namespace Platform.Tests.AssignmentsIntegration;

// ASN-04 / ASN-06 TIM 15: integracioni testovi STVARNIH PostgreSQL adaptera (port člana 1)
// nad pravom bazom: šema i migracije, atomski limit pokušaja pod istovremenim zahtevima,
// idempotentnost, optimistička provera roka i audit „oba ili nijedan” (transakcija ili outbox).
[TestFixture]
[Category("AssignmentsPostgres")]
[NonParallelizable]
public sealed class PostgresAssignmentStoresTests
{
    private const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private const string Student = "22222222-2222-2222-2222-222222222222";
    private const string SecondStudent = "33333333-3333-3333-3333-333333333333";

    private AssignmentsTestDatabase _database = null!;
    private NpgsqlDataSource _dataSource = null!;
    private PostgresAssignmentRepository _repository = null!;
    private PostgresAttemptStore _attempts = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _database = await AssignmentsTestDatabase.CreateAsync();
        _dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        _repository = new PostgresAssignmentRepository(_dataSource);
        _attempts = new PostgresAttemptStore(_dataSource, TimeProvider.System);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    // ---------- šema ----------

    [Test]
    public async Task Migrate_IsIdempotent_AndRecordsTheLatestVersion()
    {
        await AssignmentsDatabase.MigrateAsync(_database.ConnectionString);
        await AssignmentsDatabase.MigrateAsync(_database.ConnectionString);

        var versions = await _database.CountAsync("SELECT count(*) FROM assignments.schema_versions;");
        var latest = await _database.CountAsync("SELECT max(version) FROM assignments.schema_versions;");

        Assert.Multiple(() =>
        {
            Assert.That(versions, Is.EqualTo(AssignmentsDatabase.LatestVersion));
            Assert.That(latest, Is.EqualTo(AssignmentsDatabase.LatestVersion));
        });
    }

    [Test]
    public void Database_RejectsZeroAttemptLimit_EvenWithoutTheDomainModel()
    {
        var exception = Assert.ThrowsAsync<PostgresException>(async () => await _database.CountAsync("""
            INSERT INTO assignments.assignments
                (id, course_id, published_lab_revision_id, due_at_utc, max_attempts, status)
            VALUES (gen_random_uuid(), 'c', 'r', now(), 0, 1);
            SELECT 0;
            """));

        Assert.That(exception!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
    }

    // ---------- IAssignmentRepository ----------

    [Test]
    public async Task Repository_RoundTripsEveryField()
    {
        var from = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.FromHours(1));
        var due = new DateTimeOffset(2026, 12, 1, 23, 59, 0, TimeSpan.Zero);
        var assignment = new Assignment(Guid.NewGuid(), Course, "revision-7", from, due, 3, allowStartAfterDeadline: true);

        await _repository.AddAsync(assignment);
        var stored = await _repository.GetByIdAsync(assignment.Id);

        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.CourseId, Is.EqualTo(Course));
            Assert.That(stored.PublishedLabRevisionId, Is.EqualTo("revision-7"));
            Assert.That(stored.AvailableFromUtc, Is.EqualTo(from));
            Assert.That(stored.DueAtUtc, Is.EqualTo(due));
            Assert.That(stored.MaxAttempts, Is.EqualTo(3));
            Assert.That(stored.AllowStartAfterDeadline, Is.True);
            Assert.That(stored.Status, Is.EqualTo(AssignmentStatus.Active));
        });
    }

    [Test]
    public async Task Repository_UnknownId_ReturnsNull()
    {
        Assert.That(await _repository.GetByIdAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task Repository_DuplicateAdd_Throws_AndUpdatePersistsArchive()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await _repository.AddAsync(assignment));

        assignment.Archive();
        await _repository.UpdateAsync(assignment);
        var stored = await _repository.GetByIdAsync(assignment.Id);

        Assert.That(stored!.Status, Is.EqualTo(AssignmentStatus.Archived));
    }

    [Test]
    public void Repository_UpdateOfUnknownAssignment_Throws()
    {
        var assignment = NewAssignment(maxAttempts: 1);
        Assert.ThrowsAsync<KeyNotFoundException>(async () => await _repository.UpdateAsync(assignment));
    }

    [Test]
    public async Task Repository_GetForCourses_ReturnsOnlyRequestedCourses()
    {
        var mine = await AddAssignmentAsync(maxAttempts: 1, course: "d0d0d0d0-0000-4000-8000-000000000001");
        var other = await AddAssignmentAsync(maxAttempts: 1, course: "d0d0d0d0-0000-4000-8000-000000000002");

        var result = await _repository.GetForCoursesAsync(["d0d0d0d0-0000-4000-8000-000000000001"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(a => a.Id), Does.Contain(mine.Id));
            Assert.That(result.Select(a => a.Id), Does.Not.Contain(other.Id));
        });
    }

    // ---------- IAttemptStore (ASN-04) ----------

    [Test]
    public async Task Attempts_AboveLimit_ReturnsNull_AndCreatesNothing()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 2);

        var first = await StartAsync(assignment, Student, Guid.NewGuid());
        var second = await StartAsync(assignment, Student, Guid.NewGuid());
        var third = await StartAsync(assignment, Student, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));
            Assert.That(third, Is.Null);
        });
        Assert.That(await CountAttemptsAsync(assignment.Id, Student), Is.EqualTo(2));
    }

    [Test]
    public async Task Attempts_SameRequestId_ReturnsSameAttempt_EvenAfterLimitIsReached()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var requestId = Guid.NewGuid();

        var first = await StartAsync(assignment, Student, requestId);
        var retry = await StartAsync(assignment, Student, requestId);
        var other = await StartAsync(assignment, Student, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(retry, Is.EqualTo(first));
            Assert.That(other, Is.Null);
        });
        Assert.That(await CountAttemptsAsync(assignment.Id, Student), Is.EqualTo(1));
    }

    [Test]
    public async Task Attempts_ConcurrentDistinctRequests_NeverExceedTheLimit()
    {
        const int maxAttempts = 3;
        var assignment = await AddAssignmentAsync(maxAttempts);

        var results = await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => Task.Run(async () => await StartAsync(assignment, Student, Guid.NewGuid()))));

        var numbers = await _database.CountAsync("""
            SELECT count(DISTINCT attempt_number) FROM assignments.attempts
            WHERE assignment_id = @a AND student_id = @s AND attempt_number BETWEEN 1 AND 3;
            """, ("a", assignment.Id), ("s", Student));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(id => id is not null), Is.EqualTo(maxAttempts));
            Assert.That(results.Where(id => id is not null).Distinct().Count(), Is.EqualTo(maxAttempts));
            Assert.That(numbers, Is.EqualTo(maxAttempts));
        });
        Assert.That(await CountAttemptsAsync(assignment.Id, Student), Is.EqualTo(maxAttempts));
    }

    [Test]
    public async Task Attempts_ConcurrentRetriesOfOneRequest_CreateExactlyOneAttempt()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 5);
        var requestId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await StartAsync(assignment, Student, requestId))));

        Assert.Multiple(() =>
        {
            Assert.That(results.Distinct().Count(), Is.EqualTo(1));
            Assert.That(results[0], Is.Not.Null);
        });
        Assert.That(await CountAttemptsAsync(assignment.Id, Student), Is.EqualTo(1));
    }

    [Test]
    public async Task Attempts_LimitIsPerStudent()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);

        var mine = await StartAsync(assignment, Student, Guid.NewGuid());
        var theirs = await StartAsync(assignment, SecondStudent, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(mine, Is.Not.Null);
            Assert.That(theirs, Is.Not.Null);
        });
    }

    // ---------- IDeadlineExtensionStore (ASN-06) ----------

    [Test]
    public async Task Deadline_SaveThenGet_ReturnsLatest_AndKeepsHistory()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var store = CreateDeadlineStore(new RecordedTransactionalAudit());
        var firstDue = assignment.DueAtUtc.AddDays(2);
        var secondDue = assignment.DueAtUtc.AddDays(5);

        await store.SaveAsync(Override(assignment, assignment.DueAtUtc, firstDue), Entry(assignment, assignment.DueAtUtc, firstDue));
        var afterFirst = await store.GetAsync(assignment.Id, Student);
        await store.SaveAsync(Override(assignment, afterFirst!.DueAtUtc, secondDue), Entry(assignment, afterFirst.DueAtUtc, secondDue));
        var latest = await store.GetAsync(assignment.Id, Student);

        var history = await _database.CountAsync(
            "SELECT count(*) FROM assignments.deadline_overrides WHERE assignment_id = @a AND student_id = @s;",
            ("a", assignment.Id), ("s", Student));

        Assert.Multiple(() =>
        {
            Assert.That(latest!.DueAtUtc, Is.EqualTo(secondDue));
            Assert.That(latest.PreviousDueAtUtc, Is.EqualTo(firstDue));
            Assert.That(latest.ExtendedById, Is.EqualTo(Teacher));
            Assert.That(history, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Deadline_StalePreviousDeadline_Throws_AndWritesNeitherDeadlineNorAudit()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var store = CreateDeadlineStore(new RecordedTransactionalAudit());
        var stale = assignment.DueAtUtc.AddHours(-1);
        var newDue = assignment.DueAtUtc.AddDays(1);

        var outboxBefore = await CountOutboxAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Override(assignment, stale, newDue), Entry(assignment, stale, newDue)));

        Assert.That(await CountOverridesAsync(assignment.Id), Is.EqualTo(0));
        Assert.That(await CountOutboxAsync(), Is.EqualTo(outboxBefore));
        Assert.That(await store.GetAsync(assignment.Id, Student), Is.Null);
    }

    [Test]
    public async Task Deadline_TwoConcurrentExtensionsFromTheSameDeadline_OnlyOneWins()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var audit = new RecordedTransactionalAudit();

        var outcomes = await Task.WhenAll(new[] { 1, 2 }.Select(days => Task.Run(async () =>
        {
            var due = assignment.DueAtUtc.AddDays(days);
            try
            {
                await CreateDeadlineStore(audit).SaveAsync(
                    Override(assignment, assignment.DueAtUtc, due), Entry(assignment, assignment.DueAtUtc, due));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })));

        var awaited1 = await CountOverridesAsync(assignment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(outcomes.Count(won => won), Is.EqualTo(1));
            Assert.That(awaited1, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Deadline_WithRealAuditModule_StoresAuditInSameTransaction_OrInOutbox()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var correlationId = "corr-" + Guid.NewGuid().ToString("N");
        var store = CreateDeadlineStore(new PostgresTransactionalAuditWriter(TimeProvider.System), correlationId);
        var newDue = assignment.DueAtUtc.AddDays(3);

        await store.SaveAsync(Override(assignment, assignment.DueAtUtc, newDue), Entry(assignment, assignment.DueAtUtc, newDue));

        var inAudit = await _database.CountAsync(
            "SELECT count(*) FROM audit.audit_events WHERE correlation_id = @c;", ("c", correlationId));
        var inOutbox = await _database.CountAsync(
            "SELECT count(*) FROM assignments.audit_outbox WHERE correlation_id = @c AND delivered_at_utc IS NULL;",
            ("c", correlationId));

        Assert.That(await CountOverridesAsync(assignment.Id), Is.EqualTo(1));

        if (AuditEventCatalog.TryGet(AssignmentAuditEventTypes.DeadlineExtended, out _))
        {
            // Tim 12 je dodao tip: zapis je u audit.audit_events, u istoj transakciji sa rokom.
            Assert.Multiple(() =>
            {
                Assert.That(inAudit, Is.EqualTo(1));
                Assert.That(inOutbox, Is.EqualTo(0));
            });
            return;
        }

        // Tip još nije u katalogu: zapis nije izgubljen, čeka u outbox-u (Audit D3).
        var row = await ReadOutboxAsync(correlationId);
        Assert.Multiple(() =>
        {
            Assert.That(inAudit, Is.EqualTo(0));
            Assert.That(inOutbox, Is.EqualTo(1));
            Assert.That(row.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(row.Outcome, Is.EqualTo("Success"));
            Assert.That(row.ActorKind, Is.EqualTo("User"));
            Assert.That(row.ActorId, Is.EqualTo(Guid.Parse(Teacher)));
            Assert.That(row.CourseId, Is.EqualTo(Guid.Parse(Course)));
            Assert.That(row.ResourceId, Is.EqualTo(Student));
            Assert.That(row.Rejection, Is.EqualTo(nameof(AuditRejectionCode.UnknownEventType)));
            Assert.That(row.Details.Keys, Is.EquivalentTo(new[] { "previousDueAt", "newDueAt" }));
        });
    }

    [Test]
    public async Task Deadline_WhenAuditModuleRecordsInTransaction_NoOutboxRow()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var correlationId = "corr-" + Guid.NewGuid().ToString("N");
        var audit = new RecordedTransactionalAudit();
        var store = CreateDeadlineStore(audit, correlationId);
        var newDue = assignment.DueAtUtc.AddDays(3);

        await store.SaveAsync(Override(assignment, assignment.DueAtUtc, newDue), Entry(assignment, assignment.DueAtUtc, newDue));

        var inOutbox = await _database.CountAsync(
            "SELECT count(*) FROM assignments.audit_outbox WHERE correlation_id = @c;", ("c", correlationId));

        Assert.Multiple(() =>
        {
            Assert.That(audit.Calls, Is.EqualTo(1));
            Assert.That(audit.LastDraft!.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(audit.LastDraft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(audit.LastContext!.CorrelationId, Is.EqualTo(correlationId));
            Assert.That(inOutbox, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Deadline_WhenAuditModuleThrows_DeadlineAndOutboxAreCommittedTogether()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var correlationId = "corr-" + Guid.NewGuid().ToString("N");
        var store = CreateDeadlineStore(new ThrowingTransactionalAudit(), correlationId);
        var newDue = assignment.DueAtUtc.AddDays(3);

        await store.SaveAsync(Override(assignment, assignment.DueAtUtc, newDue), Entry(assignment, assignment.DueAtUtc, newDue));

        var row = await ReadOutboxAsync(correlationId);
        var awaited0 = await CountOverridesAsync(assignment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(awaited0, Is.EqualTo(1));
            Assert.That(row.Rejection, Is.EqualTo(nameof(AuditWriteStatus.Unavailable)));
        });
    }

    [Test]
    public async Task Deadline_OutboxNeverContainsSecretsOrExceptionText()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var correlationId = "corr-" + Guid.NewGuid().ToString("N");
        var store = CreateDeadlineStore(new ThrowingTransactionalAudit(), correlationId);
        var newDue = assignment.DueAtUtc.AddDays(3);

        await store.SaveAsync(Override(assignment, assignment.DueAtUtc, newDue), Entry(assignment, assignment.DueAtUtc, newDue));

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT row_to_json(o)::text FROM assignments.audit_outbox o WHERE correlation_id = @c;", connection);
        command.Parameters.AddWithValue("c", correlationId);
        var json = (string)(await command.ExecuteScalarAsync())!;

        Assert.That(json, Does.Not.Contain("password").IgnoreCase
            .And.Not.Contain("hunter2")
            .And.Not.Contain("10.0.0.5")
            .And.Not.Contain("Session").IgnoreCase);
    }

    [Test]
    public async Task Deadline_EntryForAnotherStudent_IsRejected_AndNothingIsWritten()
    {
        var assignment = await AddAssignmentAsync(maxAttempts: 1);
        var store = CreateDeadlineStore(new RecordedTransactionalAudit());
        var newDue = assignment.DueAtUtc.AddDays(1);
        var wrong = Entry(assignment, assignment.DueAtUtc, newDue) with { StudentId = SecondStudent };

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.SaveAsync(Override(assignment, assignment.DueAtUtc, newDue), wrong));

        Assert.That(await CountOverridesAsync(assignment.Id), Is.EqualTo(0));
    }

    // ---------- pomoćne metode ----------

    private PostgresDeadlineExtensionStore CreateDeadlineStore(
        ITransactionalAuditWriter audit,
        string correlationId = "assignments-store-test") =>
        new(
            _dataSource,
            audit,
            new FixedAuditContextProvider(new AuditContext(AuditActor.ForUser(Guid.Parse(Teacher)), correlationId)),
            TimeProvider.System,
            NullLogger<PostgresDeadlineExtensionStore>.Instance);

    private static Assignment NewAssignment(int maxAttempts, string course = Course) =>
        new(Guid.NewGuid(), course, "revision-1", null,
            new DateTimeOffset(DateTime.UtcNow.AddDays(7).Date, TimeSpan.Zero), maxAttempts);

    private async Task<Assignment> AddAssignmentAsync(int maxAttempts, string course = Course)
    {
        var assignment = NewAssignment(maxAttempts, course);
        await _repository.AddAsync(assignment);
        return assignment;
    }

    private async Task<Guid?> StartAsync(Assignment assignment, string student, Guid requestId) =>
        await _attempts.TryStartAsync(
            assignment.Id, student, assignment.PublishedLabRevisionId, assignment.MaxAttempts, requestId);

    private Task<long> CountAttemptsAsync(Guid assignmentId, string student) =>
        _database.CountAsync(
            "SELECT count(*) FROM assignments.attempts WHERE assignment_id = @a AND student_id = @s;",
            ("a", assignmentId), ("s", student));

    private Task<long> CountOverridesAsync(Guid assignmentId) =>
        _database.CountAsync(
            "SELECT count(*) FROM assignments.deadline_overrides WHERE assignment_id = @a;", ("a", assignmentId));

    private Task<long> CountOutboxAsync() =>
        _database.CountAsync("SELECT count(*) FROM assignments.audit_outbox;");

    private static StudentDeadlineOverride Override(Assignment assignment, DateTimeOffset previous, DateTimeOffset due) =>
        new(assignment.Id, Student, due, previous, Teacher, DateTimeOffset.UtcNow);

    private static DeadlineExtensionAuditEntry Entry(Assignment assignment, DateTimeOffset previous, DateTimeOffset due) =>
        new(DeadlineExtensionAuditOutcome.Success, assignment.Id, assignment.CourseId, Student, previous, due, ReasonCode: null);

    private async Task<OutboxRow> ReadOutboxAsync(string correlationId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT event_type, outcome, actor_kind, actor_id, course_id, resource_id, rejection, details::text
            FROM assignments.audit_outbox WHERE correlation_id = @c;
            """, connection);
        command.Parameters.AddWithValue("c", correlationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "Expected one outbox row.");

        return new OutboxRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
            reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(7))!);
    }

    private sealed record OutboxRow(
        string EventType,
        string Outcome,
        string ActorKind,
        Guid? ActorId,
        Guid? CourseId,
        string? ResourceId,
        string Rejection,
        Dictionary<string, string> Details);

    /// <summary>Dvojnik Audit modula koji „prihvata” zapis u transakciji (stanje posle izmene kataloga).</summary>
    private sealed class RecordedTransactionalAudit : ITransactionalAuditWriter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public AuditEventDraft? LastDraft { get; private set; }

        public AuditContext? LastContext { get; private set; }

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft, AuditContext context, System.Data.Common.DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            LastDraft = draft;
            LastContext = context;
            return ValueTask.FromResult(AuditWriteResult.Recorded(draft.EventId ?? Guid.NewGuid()));
        }
    }

    private sealed class ThrowingTransactionalAudit : ITransactionalAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft, AuditContext context, System.Data.Common.DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("db host=10.0.0.5 password=hunter2 SecurityLab.Session=abc");
    }
}
