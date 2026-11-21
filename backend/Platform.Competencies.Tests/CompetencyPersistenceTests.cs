using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Platform.Competencies.Composition;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;
using Platform.Competencies.Infrastructure.Persistence;

namespace Platform.Competencies.Tests;

[TestFixture]
[Category("Prg04Postgres")]
public sealed class CompetencyPersistenceTests
{
    private readonly string databaseName = "prg04_test_" + Guid.NewGuid().ToString("N");
    private string? adminConnectionString;
    private string connectionString = null!;
    private bool created;

    [OneTimeSetUp]
    public async Task CreateIsolatedDatabase()
    {
        string? configured = Environment.GetEnvironmentVariable("PRG04_TEST_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set PRG04_TEST_ADMIN_CONNECTION_STRING to run isolated PostgreSQL persistence tests.");
            return;
        }
        var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
        adminConnectionString = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        // Name contains only a fixed prefix and a generated Guid; never uses external identifiers.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        created = true;
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = OpenContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task DropOnlyOwnedTestDatabase()
    {
        if (!created) return;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private CompetenciesDbContext OpenContext() => new(new DbContextOptionsBuilder<CompetenciesDbContext>()
        .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", CompetenciesDbContext.SchemaName))
        .Options);

    [Test]
    public async Task Catalog_IsSeededAndMigrationCanRunAgainWithoutResettingLinks()
    {
        Guid laboratoryId = Guid.NewGuid();
        await using (var db = OpenContext())
            await new PostgresLaboratoryCompetencyStore(db).AddAsync(laboratoryId, BuiltInCompetencies.AccessControlId);
        await using (var db = OpenContext())
        {
            await db.Database.MigrateAsync();
            var store = new PostgresLaboratoryCompetencyStore(db);
            Assert.That((await store.ListAsync()).Select(x => x.Id), Is.EquivalentTo(BuiltInCompetencies.All.Select(x => x.Id)));
            Assert.That((await store.GetForLaboratoryAsync(laboratoryId)).Select(x => x.Id),
                Is.EqualTo(new[] { BuiltInCompetencies.AccessControlId }));
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        }
    }

    [Test]
    public async Task TwoCompetencies_SurviveDisposingAndRecreatingApplicationServices()
    {
        Guid laboratoryId = Guid.NewGuid();
        using (var app = new ServiceCollection().AddCompetencyPersistence(connectionString).BuildServiceProvider(validateScopes: true))
        {
            using var scope = app.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ILaboratoryCompetencyStore>();
            Assert.That(await store.AddAsync(laboratoryId, BuiltInCompetencies.AccessControlId), Is.True);
            Assert.That(await store.AddAsync(laboratoryId, BuiltInCompetencies.LogAnalysisId), Is.True);
        }
        using (var restartedApp = new ServiceCollection().AddCompetencyPersistence(connectionString).BuildServiceProvider(validateScopes: true))
        {
            using var scope = restartedApp.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ILaboratoryCompetencyStore>();
            Assert.That((await store.GetForLaboratoryAsync(laboratoryId)).Select(x => x.Id), Is.EquivalentTo(new[]
                { BuiltInCompetencies.AccessControlId, BuiltInCompetencies.LogAnalysisId }));
        }
    }

    [Test]
    public async Task DuplicateLink_IsIdempotentAndDatabaseKeyAlsoRejectsDirectDuplicate()
    {
        Guid laboratoryId = Guid.NewGuid();
        await using var db = OpenContext();
        var store = new PostgresLaboratoryCompetencyStore(db);
        Assert.That(await store.AddAsync(laboratoryId, BuiltInCompetencies.AccessControlId), Is.True);
        Assert.That(await store.AddAsync(laboratoryId, BuiltInCompetencies.AccessControlId), Is.False);
        Assert.That(await db.LaboratoryCompetencies.CountAsync(x => x.LaboratoryId == laboratoryId), Is.EqualTo(1));
        var exception = Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO prg04.laboratory_competencies ("LaboratoryId", "CompetencyId")
            VALUES ({laboratoryId}, {BuiltInCompetencies.AccessControlId})
            """));
        Assert.That(exception!.SqlState, Is.EqualTo(PostgresErrorCodes.UniqueViolation));
    }

    [Test]
    public async Task ConcurrentDuplicateAdds_CreateExactlyOneRelationship()
    {
        Guid laboratoryId = Guid.NewGuid();
        await using var first = OpenContext();
        await using var second = OpenContext();
        bool[] outcomes = await Task.WhenAll(
            new PostgresLaboratoryCompetencyStore(first).AddAsync(laboratoryId, BuiltInCompetencies.LogAnalysisId),
            new PostgresLaboratoryCompetencyStore(second).AddAsync(laboratoryId, BuiltInCompetencies.LogAnalysisId));
        Assert.That(outcomes.Count(added => added), Is.EqualTo(1));
        await using var verify = OpenContext();
        Assert.That((await new PostgresLaboratoryCompetencyStore(verify).GetForLaboratoryAsync(laboratoryId)), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RemoveOneLink_PreservesOtherLinksAndSharedCatalogEntry()
    {
        Guid firstLab = Guid.NewGuid();
        Guid otherLab = Guid.NewGuid();
        await using (var db = OpenContext())
        {
            var store = new PostgresLaboratoryCompetencyStore(db);
            await store.AddAsync(firstLab, BuiltInCompetencies.AccessControlId);
            await store.AddAsync(firstLab, BuiltInCompetencies.LogAnalysisId);
            await store.AddAsync(otherLab, BuiltInCompetencies.AccessControlId);
            Assert.That(await store.RemoveAsync(firstLab, BuiltInCompetencies.AccessControlId), Is.True);
            Assert.That(await store.RemoveAsync(firstLab, BuiltInCompetencies.AccessControlId), Is.False);
        }
        await using var restored = OpenContext();
        var reader = new PostgresLaboratoryCompetencyStore(restored);
        Assert.That((await reader.GetForLaboratoryAsync(firstLab)).Select(x => x.Id), Is.EqualTo(new[] { BuiltInCompetencies.LogAnalysisId }));
        Assert.That((await reader.GetForLaboratoryAsync(otherLab)).Select(x => x.Id), Is.EqualTo(new[] { BuiltInCompetencies.AccessControlId }));
        Assert.That(await reader.ListAsync(), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task UnknownCompetency_IsRejectedByStoreAndForeignKey()
    {
        Guid laboratoryId = Guid.NewGuid();
        Guid unknown = Guid.NewGuid();
        await using var db = OpenContext();
        var store = new PostgresLaboratoryCompetencyStore(db);
        Assert.ThrowsAsync<KeyNotFoundException>(() => store.AddAsync(laboratoryId, unknown));
        var exception = Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO prg04.laboratory_competencies ("LaboratoryId", "CompetencyId")
            VALUES ({laboratoryId}, {unknown})
            """));
        Assert.That(exception!.SqlState, Is.EqualTo(PostgresErrorCodes.ForeignKeyViolation));
        Assert.That(await store.GetForLaboratoryAsync(laboratoryId), Is.Empty);
    }

    [Test]
    public async Task DatabaseConstraints_RejectEmptyLaboratoryIdAndProtectCatalogFromLinkedDeletion()
    {
        Guid laboratoryId = Guid.NewGuid();
        await using var db = OpenContext();
        var emptyId = Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO prg04.laboratory_competencies ("LaboratoryId", "CompetencyId")
            VALUES ({Guid.Empty}, {BuiltInCompetencies.AccessControlId})
            """));
        Assert.That(emptyId!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        await new PostgresLaboratoryCompetencyStore(db).AddAsync(laboratoryId, BuiltInCompetencies.AccessControlId);
        var deletion = Assert.ThrowsAsync<PostgresException>(() => db.Competencies
            .Where(x => x.Id == BuiltInCompetencies.AccessControlId).ExecuteDeleteAsync());
        Assert.That(deletion!.SqlState, Is.EqualTo(PostgresErrorCodes.ForeignKeyViolation));
    }

    [Test]
    public async Task Migration_CreatesOnlyPrg04Tables()
    {
        await using var db = OpenContext();
        var tables = await db.Database.SqlQueryRaw<string>("""
            SELECT schemaname || '.' || tablename AS "Value" FROM pg_tables
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            """).ToListAsync();
        Assert.That(tables, Is.EquivalentTo(new[]
            { "prg04.competencies", "prg04.laboratory_competencies", "prg04.__EFMigrationsHistory" }));
    }
}
