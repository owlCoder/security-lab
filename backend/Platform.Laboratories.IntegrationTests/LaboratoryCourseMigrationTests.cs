using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;

namespace Platform.Laboratories.IntegrationTests;

public partial class PublicationDatabaseTests
{
    [Test]
    public async Task LaboratoryCourseMigration_PreservesLegacyRowsAndRejectsEmptyCourse()
    {
        var connection = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            Database = "legacy_course_upgrade"
        };
        await using (var admin = new NpgsqlConnection(database.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE legacy_course_upgrade", admin);
            await create.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<LaboratoryDbContext>()
            .UseNpgsql(connection.ConnectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "authoring")).Options;
        var id = Guid.NewGuid();
        var author = Guid.NewGuid();
        await using (var previous = new LaboratoryDbContext(options))
        {
            await previous.GetService<IMigrator>().MigrateAsync("20261104213758_AlignLaboratoryPersistence");
            await previous.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO authoring.laboratories ("Id","Title","Status","CreatedBy","CreatedAt","Tags")
                VALUES ({id}, {"Legacy lab"}, {"Draft"}, {author}, {DateTimeOffset.UtcNow}, ARRAY[]::text[])
                """);
        }

        await using var upgraded = new LaboratoryDbContext(options);
        await upgraded.Database.MigrateAsync();
        await upgraded.Database.MigrateAsync();
        Assert.That(upgraded.Database.HasPendingModelChanges(), Is.False);
        var store = new PostgresLaboratoryStore(upgraded);
        var row = await store.GetByIdAsync(id);
        Assert.That(row!.CourseId, Is.Null);
        Assert.That(row.CreatedBy, Is.EqualTo(author));
        Assert.That(row.Title, Is.EqualTo("Legacy lab"));
        var repository = new PostgresLaboratoryRepository(store);
        var legacy = await repository.GetByIdAsync(id);
        Assert.That(legacy!.CourseId, Is.EqualTo(Guid.Empty));
        legacy.Rename("Legacy renamed");
        await repository.UpdateAsync(legacy);
        Assert.That((await store.GetByIdAsync(id))!.CourseId, Is.Null);
        Assert.ThrowsAsync<PostgresException>(async () =>
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE authoring.laboratories SET "CourseId" = {Guid.Empty} WHERE "Id" = {id}
                """));
    }
}
