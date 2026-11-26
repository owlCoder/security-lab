using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-04 (Antonić, KT2-09): odvajanje ključa provereno nad pravom PostgreSQL bazom, ne samo nad InMemory.
// Baza je izolovana i pravi je scripts/test-labvisibility-postgres.ps1; bez promenljive testovi se preskaču.
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class LabVisibilityPostgresTests
{
    // Samo posebna TEST baza. Nikad ne usmeravati na deljenu ili produkcionu bazu.
    public const string ConnectionVariable = "LABVISIBILITY_TEST_POSTGRES";

    private const string SecretAnswer = "SECRET-ANSWER-42";
    private const string KeyTable = "lab_evaluator_key";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<Guid> createdLabs = [];

    private static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionVariable);

    [OneTimeSetUp]
    public async Task MigrateDatabase()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Assert.Ignore($"{ConnectionVariable} nije podešen; testovi nad PostgreSQL-om su preskočeni.");

        await using var db = new PostgresDbFactory().CreateDbContext();
        await db.Database.MigrateAsync();
    }

    [TearDown]
    public async Task DeleteTestLabs()
    {
        if (createdLabs.Count == 0)
            return;

        var ids = createdLabs.ToArray();
        createdLabs.Clear();

        // Kaskada briše i zadatke i ključ.
        await using var db = new PostgresDbFactory().CreateDbContext();
        await db.PublicContents.Where(x => ids.Contains(x.LabId)).ExecuteDeleteAsync();
    }

    // ---- Šema u pravoj bazi ----

    [Test]
    public async Task Database_AnswerColumnExistsOnlyInEvaluatorKeyTable()
    {
        await using var db = new PostgresDbFactory().CreateDbContext();

        var tables = await db.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'lab_visibility' AND column_name ILIKE '%answer%'")
            .ToListAsync();

        Assert.That(tables, Is.EqualTo(new[] { KeyTable }));
    }

    // ---- Javni reader ----

    [Test]
    public async Task Reader_ReadsLabFromPostgres_WithoutAnswer()
    {
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, Answers());

        var read = await new EfLabPublicContentReader(dbFactory).GetAsync(content.LabId);

        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(read!.Title, Is.EqualTo(content.Title));
            Assert.That(read.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "t1", "t2" }));
            Assert.That(read.Tasks.First().Options, Is.EqualTo(new[] { "A", "B" }), "text[] mora da se vrati ceo i u redu.");
            Assert.That(JsonSerializer.Serialize(read), Does.Not.Contain(SecretAnswer));
        });
    }

    [Test]
    public async Task ReaderSql_NeverTouchesEvaluatorKeyTable()
    {
        // NEGATIVNI TEST (KT2-09): hvata se svaki SQL koji javni reader pošalje bazi.
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, Answers());
        dbFactory.Sql.Clear();

        var reader = new EfLabPublicContentReader(dbFactory);
        await reader.GetAsync(content.LabId);
        await reader.GetAsync(Guid.NewGuid());

        Assert.That(dbFactory.Sql, Is.Not.Empty, "Reader mora da pita bazu; inače test ništa ne dokazuje.");
        Assert.That(dbFactory.Sql.Where(sql => sql.Contains(KeyTable, StringComparison.OrdinalIgnoreCase)), Is.Empty);
    }

    [Test]
    public async Task KeyStoreSql_DoesTouchEvaluatorKeyTable()
    {
        // Kontrola: hvatanje SQL-a radi, pa prethodni test nije prošao slučajno.
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, Answers());
        dbFactory.Sql.Clear();

        await new EfEvaluatorKeyStore(dbFactory).GetAsync(content.LabId);

        Assert.That(dbFactory.Sql.Any(sql => sql.Contains(KeyTable, StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    // ---- Store i transakcija ----

    [Test]
    public async Task Store_SaveThenGet_RoundTripsKey()
    {
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, answers: null);
        var store = new EfEvaluatorKeyStore(dbFactory);

        await store.SaveAsync(EvaluatorKey.Create(content, Answers()));
        var key = await store.GetAsync(content.LabId);

        Assert.That(key, Is.Not.Null);
        Assert.That(
            key!.Answers.Select(a => (a.TaskId, a.ExpectedAnswer)),
            Is.EquivalentTo(new[] { ("t1", SecretAnswer), ("t2", "C") }));
    }

    [Test]
    public async Task Store_Save_ReplacesWholeKey()
    {
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, Answers());

        await new EfEvaluatorKeyStore(dbFactory).SaveAsync(
            EvaluatorKey.Create(content, [new TaskAnswerKey("t1", "NEW-1"), new TaskAnswerKey("t2", "NEW-2")]));

        await using var db = dbFactory.CreateDbContext();
        var rows = await db.EvaluatorKeys.Where(x => x.LabId == content.LabId).ToListAsync();

        Assert.That(rows.Select(r => r.ExpectedAnswer), Is.EquivalentTo(new[] { "NEW-1", "NEW-2" }));
    }

    [Test]
    public async Task Store_FailedSave_RollsBackAndKeepsOldKey()
    {
        // NEGATIVNI TEST: brisanje starog ključa i upis novog su jedna transakcija.
        // Novi ključ pokazuje na zadatak koji ne postoji u bazi, pa upis pada na stranom ključu
        // posle brisanja; stari ključ mora da ostane ceo.
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, Answers());

        var notInDatabase = LabPublicContent.Create(
            content.LabId, CourseId, TeacherId, LabVisibilityState.Draft, "Druga verzija", "Cilj",
            [new PublicTask("t3", 1, "Zadatak koji nije u bazi?", ["X"])]);
        var badKey = EvaluatorKey.Create(notInDatabase, [new TaskAnswerKey("t3", "X")]);

        Assert.ThrowsAsync<DbUpdateException>(async () =>
            await new EfEvaluatorKeyStore(dbFactory).SaveAsync(badKey));

        var kept = await new EfEvaluatorKeyStore(dbFactory).GetAsync(content.LabId);
        Assert.That(kept, Is.Not.Null, "Rollback nije vratio obrisan ključ.");
        Assert.That(kept!.Answers.Select(a => a.ExpectedAnswer), Is.EquivalentTo(new[] { SecretAnswer, "C" }));
    }

    [Test]
    public async Task Store_IncompleteKeyInDatabase_IsRejected()
    {
        var dbFactory = new PostgresDbFactory();
        var content = PublishedLab();
        await SeedAsync(dbFactory, content, [new TaskAnswerKey("t1", SecretAnswer)]);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await new EfEvaluatorKeyStore(dbFactory).GetAsync(content.LabId));
    }

    // ---- Pomoćne metode ----

    private LabPublicContent PublishedLab()
    {
        var labId = Guid.NewGuid();
        createdLabs.Add(labId);

        return LabPublicContent.Create(
            labId, CourseId, TeacherId, LabVisibilityState.Published,
            "Analiza dnevnika", "Pronađi povezane događaje",
            [
                new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"]),
                new PublicTask("t2", 2, "Koji je izvor?", ["C", "D"])
            ]);
    }

    private static TaskAnswerKey[] Answers() =>
        [new TaskAnswerKey("t1", SecretAnswer), new TaskAnswerKey("t2", "C")];

    // Upis direktno kroz redove, da i nepotpun ključ može da se postavi u bazu.
    private static async Task SeedAsync(
        IDbContextFactory<LabVisibilityDbContext> dbFactory,
        LabPublicContent content,
        TaskAnswerKey[]? answers)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.PublicContents.Add(new LabPublicContentRow
        {
            LabId = content.LabId,
            CourseId = content.CourseId,
            AuthorId = content.AuthorId,
            State = content.State.ToString(),
            Title = content.Title,
            Goal = content.Goal,
            Tasks = content.Tasks
                .Select(t => new LabPublicTaskRow
                {
                    LabId = content.LabId,
                    TaskId = t.TaskId,
                    Order = t.Order,
                    Prompt = t.Prompt,
                    Options = t.Options.ToList()
                })
                .ToList()
        });

        foreach (var answer in answers ?? Array.Empty<TaskAnswerKey>())
        {
            db.EvaluatorKeys.Add(new EvaluatorKeyRow
            {
                LabId = content.LabId,
                TaskId = answer.TaskId,
                ExpectedAnswer = answer.ExpectedAnswer
            });
        }

        await db.SaveChangesAsync();
    }

    // Svaki poziv pravi nov DbContext nad test bazom, kao AddDbContextFactory u aplikaciji,
    // i pamti tekst svakog SQL-a koji ode u bazu.
    private sealed class PostgresDbFactory : IDbContextFactory<LabVisibilityDbContext>
    {
        private readonly SqlCapture capture = new();

        public List<string> Sql => capture.Commands;

        public LabVisibilityDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<LabVisibilityDbContext>()
                .UseNpgsql(
                    ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", LabVisibilityDbContext.Schema))
                .AddInterceptors(capture)
                .Options);
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
