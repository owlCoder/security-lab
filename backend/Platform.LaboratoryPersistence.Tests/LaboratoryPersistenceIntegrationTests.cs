using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;

namespace Platform.LaboratoryPersistence.Tests;

[TestFixture]
[Category("Integration")]
public class LaboratoryPersistenceIntegrationTests
{
    [SetUp]
    public void RequireDatabase()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Authoring")))
            Assert.Ignore("ConnectionStrings__Authoring is not configured; PostgreSQL integration tests skipped.");
    }

    private static LaboratoryDbContext CreateContext()
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Authoring")
            ?? throw new InvalidOperationException("ConnectionStrings__Authoring is required.");
        var options = new DbContextOptionsBuilder<LaboratoryDbContext>().UseNpgsql(cs).Options;
        return new LaboratoryDbContext(options);
    }

    // LAB-03: konstruktor sada trazi kurs; minimalna izmena da build prodje (vidi docs/components/LAB-03.md).
    private static Laboratory NewLab(string title) => new(Guid.NewGuid(), title, Guid.NewGuid());

    private static async Task DeleteLabs(params Guid[] ids)
    {
        await using var db = CreateContext();
        await db.LabPrerequisites.Where(x => ids.Contains(x.LaboratoryId) || ids.Contains(x.PrerequisiteLabId))
            .ExecuteDeleteAsync();
        await db.LabRevisions.Where(x => ids.Contains(x.LaboratoryId)).ExecuteDeleteAsync();
        await db.Laboratories.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Test]
    public async Task DatabaseConnectionWorks()
    {
        await using var db = CreateContext();
        Assert.That(await db.Database.CanConnectAsync(), Is.True);
    }

    [Test]
    public async Task Lab01IdentitySurvivesNewContext()
    {
        var lab = NewLab("Persistent LAB-01");
        try
        {
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).AddAsync(lab);
            await using (var db = CreateContext())
            {
                var result = await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db))
                    .GetByIdAsync(lab.Id);
                Assert.That(result, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(result!.Id, Is.EqualTo(lab.Id));
                    Assert.That(result.CreatedBy, Is.EqualTo(lab.CreatedBy));
                    Assert.That((result.CreatedAt - lab.CreatedAt).Duration(), Is.LessThan(TimeSpan.FromMilliseconds(1)));
                    Assert.That(result.Status, Is.EqualTo(LabStatus.Draft));
                });
            }
        }
        finally { await DeleteLabs(lab.Id); }
    }

    [Test]
    public async Task RenameKeepsIdentityAndCreator()
    {
        var lab = NewLab("Before");
        try
        {
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).AddAsync(lab);
            lab.Rename("After");
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).UpdateAsync(lab);
            await using (var db = CreateContext())
            {
                var result = await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).GetByIdAsync(lab.Id);
                Assert.That(result!.Title, Is.EqualTo("After"));
                Assert.That(result.Id, Is.EqualTo(lab.Id));
                Assert.That(result.CreatedBy, Is.EqualTo(lab.CreatedBy));
            }
        }
        finally { await DeleteLabs(lab.Id); }
    }

    [Test]
    public async Task Lab02MetadataUsesExistingLab01Identity()
    {
        var lab = NewLab("LAB-02");
        try
        {
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).AddAsync(lab);
            var metadata = new LaboratoryMetadata(lab.Id, "Learn access checks",
                LaboratoryDifficulty.Medium, LaboratoryType.AccessControl, new[] { "security", "access" });
            await using (var db = CreateContext())
                await new PostgresLaboratoryStore(db).SaveAsync(metadata);
            await using (var db = CreateContext())
            {
                var result = await new PostgresLaboratoryStore(db).GetAsync(lab.Id);
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.LaboratoryId, Is.EqualTo(lab.Id));
                Assert.That(result.Objective, Is.EqualTo("Learn access checks"));
                Assert.That(result.Difficulty, Is.EqualTo(LaboratoryDifficulty.Medium));
                Assert.That(result.Tags, Does.Contain("security"));
            }
        }
        finally { await DeleteLabs(lab.Id); }
    }

    [Test]
    public async Task RevisionNumbersPersistAcrossContexts()
    {
        var lab = NewLab("Revision lab");
        try
        {
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).AddAsync(lab);
            await using (var db = CreateContext())
                Assert.That((await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db))
                    .AddRevisionAsync(lab.Id)).Number, Is.EqualTo(1));
            await using (var db = CreateContext())
                Assert.That((await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db))
                    .AddRevisionAsync(lab.Id)).Number, Is.EqualTo(2));
            await using (var db = CreateContext())
                Assert.That((await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db))
                    .GetRevisionsAsync(lab.Id)).Select(x => x.Number), Is.EqualTo(new[] { 1, 2 }));
        }
        finally { await DeleteLabs(lab.Id); }
    }

    [Test]
    public async Task PrerequisitesCanBeAddedReadAndRemoved()
    {
        var a = NewLab("Prereq A");
        var b = NewLab("Prereq B");
        try
        {
            await using (var db = CreateContext())
            {
                var repo = new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db));
                await repo.AddAsync(a);
                await repo.AddAsync(b);
                await new PostgresLaboratoryStore(db).AddAsync(b.Id, a.Id);
            }
            await using (var db = CreateContext())
                Assert.That(await new PostgresLaboratoryStore(db).GetIdsAsync(b.Id), Is.EquivalentTo(new[] { a.Id }));
            await using (var db = CreateContext())
                await new PostgresLaboratoryStore(db).RemoveAsync(b.Id, a.Id);
            await using (var db = CreateContext())
                Assert.That(await new PostgresLaboratoryStore(db).GetIdsAsync(b.Id), Is.Empty);
        }
        finally { await DeleteLabs(a.Id, b.Id); }
    }

    [Test]
    public async Task SelfPrerequisiteRejected()
    {
        var lab = NewLab("Self test");
        try
        {
            await using var db = CreateContext();
            var repo = new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db));
            await repo.AddAsync(lab);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await new PostgresLaboratoryStore(db).AddAsync(lab.Id, lab.Id));
        }
        finally { await DeleteLabs(lab.Id); }
    }

    [Test]
    public async Task DuplicatePrerequisiteRejectedByDatabase()
    {
        var a = NewLab("A");
        var b = NewLab("B");
        try
        {
            await using (var db = CreateContext())
            {
                var repo = new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db));
                await repo.AddAsync(a);
                await repo.AddAsync(b);
                await new PostgresLaboratoryStore(db).AddAsync(b.Id, a.Id);
            }
            await using var newDb = CreateContext();
            Assert.ThrowsAsync<DbUpdateException>(async () =>
                await new PostgresLaboratoryStore(newDb).AddAsync(b.Id, a.Id));
        }
        finally { await DeleteLabs(a.Id, b.Id); }
    }

    [Test]
    public async Task MissingPrerequisiteRejectedByDatabase()
    {
        var a = NewLab("A");
        try
        {
            await using (var db = CreateContext())
                await new PostgresLaboratoryRepository(new PostgresLaboratoryStore(db)).AddAsync(a);
            await using var db2 = CreateContext();
            Assert.ThrowsAsync<DbUpdateException>(async () =>
                await new PostgresLaboratoryStore(db2).AddAsync(a.Id, Guid.NewGuid()));
        }
        finally { await DeleteLabs(a.Id); }
    }
}
