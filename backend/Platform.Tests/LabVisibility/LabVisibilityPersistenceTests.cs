using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Composition;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-04 (Antonić): javni sadržaj i evaluator ključ su odvojeni i u bazi, ne samo u kodu.
public class LabVisibilityPersistenceTests
{
    private const string SecretAnswer = "SECRET-ANSWER-42";
    private static readonly string[] AnswerLikeWords = ["answer", "expected", "solution", "correct"];
    private static readonly Guid LabId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- EF model: šta fizički postoji u kojoj tabeli ----

    // Veza ka bazi se nikad ne otvara: čitamo samo model koji EF pravi iz OnModelCreating.
    private static LabVisibilityDbContext NpgsqlModelOnly() =>
        new(new DbContextOptionsBuilder<LabVisibilityDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options);

    [TestCase("lab_public_content")]
    [TestCase("lab_public_task")]
    public void PublicTable_HasNoAnswerColumn(string tableName)
    {
        using var db = NpgsqlModelOnly();
        var table = db.Model.GetRelationalModel().FindTable(tableName, LabVisibilityDbContext.Schema);

        Assert.That(table, Is.Not.Null, $"Tabela {tableName} ne postoji u modelu.");

        var suspicious = table!.Columns
            .Select(c => c.Name)
            .Where(name => AnswerLikeWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.That(suspicious, Is.Empty);
    }

    [Test]
    public void AnswerColumn_ExistsOnlyInEvaluatorKeyTable()
    {
        using var db = NpgsqlModelOnly();
        var tablesWithAnswer = db.Model.GetRelationalModel().Tables
            .Where(t => t.Columns.Any(c => c.Name == "expected_answer"))
            .Select(t => t.Name)
            .ToList();

        Assert.That(tablesWithAnswer, Is.EqualTo(new[] { "lab_evaluator_key" }));
    }

    [Test]
    public void PublicRows_HaveNoNavigationToEvaluatorKey()
    {
        // Bez navigacije, Include nad javnim redovima ne može da povuče odgovore.
        using var db = NpgsqlModelOnly();

        foreach (var type in new[] { typeof(LabPublicContentRow), typeof(LabPublicTaskRow) })
        {
            var targets = db.Model.FindEntityType(type)!
                .GetNavigations()
                .Select(n => n.TargetEntityType.ClrType);

            Assert.That(targets, Has.None.EqualTo(typeof(EvaluatorKeyRow)), type.Name);
        }
    }

    // ---- Ugovor: ILabPublicContentReader ne može ni da nosi ključ ----

    [Test]
    public void PublicContentReaderContract_CannotCarryKey()
    {
        var forbidden = new[] { typeof(EvaluatorKey), typeof(TaskAnswerKey), typeof(EvaluatorKeyRow) };
        var reachable = ReachableTypes(typeof(LabPublicContent));

        var forbiddenTypes = reachable.Where(x => forbidden.Contains(x.Type)).ToList();
        var answerLikeProperties = reachable
            .Where(x => x.Property.Contains("Answer", StringComparison.OrdinalIgnoreCase)
                     || x.Property.Contains("Key", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(forbiddenTypes, Is.Empty);
            Assert.That(answerLikeProperties, Is.Empty);
        });
    }

    // ---- Ponašanje EF reader-a i store-a (EF InMemory provider, bez Dockera) ----

    [Test]
    public async Task EfReader_ReturnsPublicContent_WithoutAnswer()
    {
        var dbFactory = new InMemoryDbFactory();
        await SeedRowsAsync(dbFactory, keyForAllTasks: true);

        var content = await new EfLabPublicContentReader(dbFactory).GetAsync(LabId);

        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "t1", "t2" }));
        Assert.That(JsonSerializer.Serialize(content), Does.Not.Contain(SecretAnswer));
    }

    [Test]
    public async Task EfKeyStore_ReadsAnswerFromSeparateTable()
    {
        // Kontrola: odgovor zaista jeste u bazi, pa prethodni test nije prošao slučajno.
        var dbFactory = new InMemoryDbFactory();
        await SeedRowsAsync(dbFactory, keyForAllTasks: true);

        var key = await new EfEvaluatorKeyStore(dbFactory).GetAsync(LabId);

        Assert.That(key, Is.Not.Null);
        Assert.That(key!.Answers.Select(a => a.ExpectedAnswer), Has.Member(SecretAnswer));
        Assert.That(key.ToString(), Does.Not.Contain(SecretAnswer));
    }

    [Test]
    public async Task EfKeyStore_RejectsIncompleteKeyFromDatabase()
    {
        // Ako neko ručno obriše odgovor iz baze, store baca grešku umesto da vrati pola ključa.
        var dbFactory = new InMemoryDbFactory();
        await SeedRowsAsync(dbFactory, keyForAllTasks: false);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await new EfEvaluatorKeyStore(dbFactory).GetAsync(LabId));
    }

    [Test]
    public async Task EfReaderAndStore_UnknownLab_ReturnNull()
    {
        var dbFactory = new InMemoryDbFactory();

        Assert.That(await new EfLabPublicContentReader(dbFactory).GetAsync(Guid.NewGuid()), Is.Null);
        Assert.That(await new EfEvaluatorKeyStore(dbFactory).GetAsync(Guid.NewGuid()), Is.Null);
    }

    // ---- Registracija: EF samo sa connection string-om, i tada kao singleton ----

    [Test]
    public void WithoutConnectionString_UsesInMemory()
    {
        using var provider = BuildProvider(connectionString: null);

        Assert.That(provider.GetRequiredService<ILabPublicContentReader>(), Is.InstanceOf<InMemoryLabPublicContentReader>());
        Assert.That(provider.GetRequiredService<IEvaluatorKeyStore>(), Is.InstanceOf<InMemoryEvaluatorKeyStore>());
    }

    [Test]
    public void WithConnectionString_UsesEf_AndSingletonsResolveWithScopeValidation()
    {
        using var provider = BuildProvider("Host=localhost;Database=never-opened");

        var reader = provider.GetRequiredService<ILabPublicContentReader>();

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.InstanceOf<EfLabPublicContentReader>());
            Assert.That(provider.GetRequiredService<ILabPublicContentReader>(), Is.SameAs(reader));
            Assert.That(provider.GetRequiredService<IEvaluatorKeyStore>(), Is.InstanceOf<EfEvaluatorKeyStore>());
            // Singleton koji zavisi od store-a (kartica 5) mora da se napravi bez greške o scope-u.
            Assert.That(() => provider.GetRequiredService<EvaluatorKeyReader>(), Throws.Nothing);
        });
    }

    // ---- Pomoćne metode ----

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var settings = new Dictionary<string, string?>();
        if (connectionString is not null)
            settings[$"ConnectionStrings:{LabVisibilityDatabase.ConnectionStringName}"] = connectionString;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        services.AddAccessControl();
        services.AddLabVisibility();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static async Task SeedRowsAsync(IDbContextFactory<LabVisibilityDbContext> dbFactory, bool keyForAllTasks)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.PublicContents.Add(new LabPublicContentRow
        {
            LabId = LabId,
            CourseId = CourseId,
            AuthorId = TeacherId,
            State = nameof(LabVisibilityState.Published),
            Title = "Analiza dnevnika",
            Goal = "Pronađi povezane događaje",
            Tasks =
            [
                new LabPublicTaskRow { LabId = LabId, TaskId = "t1", Order = 1, Prompt = "Koji događaj je prvi?", Options = ["A", "B"] },
                new LabPublicTaskRow { LabId = LabId, TaskId = "t2", Order = 2, Prompt = "Koji je izvor?", Options = ["C", "D"] }
            ]
        });

        db.EvaluatorKeys.Add(new EvaluatorKeyRow { LabId = LabId, TaskId = "t1", ExpectedAnswer = SecretAnswer });
        if (keyForAllTasks)
            db.EvaluatorKeys.Add(new EvaluatorKeyRow { LabId = LabId, TaskId = "t2", ExpectedAnswer = "C" });

        await db.SaveChangesAsync();
    }

    // Svi tipovi i imena svojstava do kojih se stiže od LabPublicContent (uključujući elemente listi).
    private static List<(Type Type, string Property)> ReachableTypes(Type root)
    {
        var result = new List<(Type, string)>();
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>([root]);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!visited.Add(type))
                continue;

            foreach (var arg in type.GetGenericArguments())
                queue.Enqueue(arg);

            if (type.Assembly != typeof(LabPublicContent).Assembly)
                continue;

            foreach (var property in type.GetProperties())
            {
                result.Add((property.PropertyType, property.Name));
                queue.Enqueue(property.PropertyType);
            }
        }

        return result;
    }

    // Svaka instanca ima svoju praznu InMemory bazu, pa se testovi ne mešaju.
    private sealed class InMemoryDbFactory : IDbContextFactory<LabVisibilityDbContext>
    {
        private readonly string name = Guid.NewGuid().ToString("N");

        public LabVisibilityDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<LabVisibilityDbContext>()
                .UseInMemoryDatabase(name)
                .Options);
    }
}
