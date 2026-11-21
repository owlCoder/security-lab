using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PrerequisiteDatabaseTests
{
    private const string ConnectionVariable = "ConnectionStrings__Authoring";
    private readonly List<Guid> _createdLabs = new();

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable);

    [OneTimeSetUp]
    public async Task PrepareDatabase()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Assert.Ignore($"{ConnectionVariable} nije podesen; testovi sa bazom su preskoceni.");
        }

        await using LaboratoryDbContext db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [TearDown]
    public async Task DeleteTestLaboratories()
    {
        if (_createdLabs.Count == 0)
        {
            return;
        }

        Guid[] ids = _createdLabs.ToArray();
        _createdLabs.Clear();

        await using LaboratoryDbContext db = CreateContext();
        await db.LabPrerequisites
            .Where(x => ids.Contains(x.LaboratoryId) || ids.Contains(x.PrerequisiteLabId))
            .ExecuteDeleteAsync();
        await db.Laboratories
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync();
    }

    [Test]
    public async Task AddAsync_CycleInDatabase_IsRejected()
    {
        Guid labA = await CreateLaboratoryAsync();
        Guid labB = await CreateLaboratoryAsync();
        await using LaboratoryDbContext db = CreateContext();
        var adapter = new LabPrerequisiteStoreAdapter(new PostgresLaboratoryStore(db));

        AddPrerequisiteResult first = await adapter.AddAsync(new LabPrerequisite(labA, labB));
        AddPrerequisiteResult second = await adapter.AddAsync(new LabPrerequisite(labB, labA));
        int saved = await CountEdgesAsync(labA, labB);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(AddPrerequisiteResult.Added));
            Assert.That(second, Is.EqualTo(AddPrerequisiteResult.WouldCreateCycle));
            Assert.That(saved, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddAsync_TwoTeachersAtSameTime_OnlyOneDirectionIsSaved()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Guid labA = await CreateLaboratoryAsync();
            Guid labB = await CreateLaboratoryAsync();
            await using LaboratoryDbContext firstDb = CreateContext();
            await using LaboratoryDbContext secondDb = CreateContext();
            var firstTeacher = new LabPrerequisiteStoreAdapter(new PostgresLaboratoryStore(firstDb));
            var secondTeacher = new LabPrerequisiteStoreAdapter(new PostgresLaboratoryStore(secondDb));

            AddPrerequisiteResult[] results = await Task.WhenAll(
                firstTeacher.AddAsync(new LabPrerequisite(labA, labB)),
                secondTeacher.AddAsync(new LabPrerequisite(labB, labA)));
            int saved = await CountEdgesAsync(labA, labB);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.EquivalentTo(new[]
                {
                    AddPrerequisiteResult.Added,
                    AddPrerequisiteResult.WouldCreateCycle
                }));
                Assert.That(saved, Is.EqualTo(1));
            });
        }
    }

    [Test]
    public async Task RemoveAsync_ExistingPrerequisite_IsDeletedFromDatabase()
    {
        Guid labA = await CreateLaboratoryAsync();
        Guid labB = await CreateLaboratoryAsync();
        await using LaboratoryDbContext db = CreateContext();
        var adapter = new LabPrerequisiteStoreAdapter(new PostgresLaboratoryStore(db));
        await adapter.AddAsync(new LabPrerequisite(labA, labB));

        bool removed = await adapter.RemoveAsync(new LabPrerequisite(labA, labB));
        int remaining = await CountEdgesAsync(labA, labB);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(remaining, Is.EqualTo(0));
        });
    }

    private static LaboratoryDbContext CreateContext()
    {
        DbContextOptions<LaboratoryDbContext> options =
            new DbContextOptionsBuilder<LaboratoryDbContext>()
                .UseNpgsql(ConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "authoring"))
                .Options;

        return new LaboratoryDbContext(options);
    }

    private async Task<Guid> CreateLaboratoryAsync()
    {
        Guid id = Guid.NewGuid();
        await using LaboratoryDbContext db = CreateContext();
        db.Laboratories.Add(new LaboratoryRecord
        {
            Id = id,
            Title = "LAB-06 test",
            Status = "Draft",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        _createdLabs.Add(id);
        return id;
    }

    private static async Task<int> CountEdgesAsync(Guid first, Guid second)
    {
        await using LaboratoryDbContext db = CreateContext();
        return await db.LabPrerequisites.CountAsync(x =>
            (x.LaboratoryId == first && x.PrerequisiteLabId == second) ||
            (x.LaboratoryId == second && x.PrerequisiteLabId == first));
    }
}