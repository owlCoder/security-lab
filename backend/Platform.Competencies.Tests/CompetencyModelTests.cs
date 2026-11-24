using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Competencies.Composition;
using Platform.Competencies.Contracts;
using Platform.Competencies.Infrastructure.Persistence;

namespace Platform.Competencies.Tests;

[TestFixture]
public sealed class CompetencyModelTests
{
    [Test]
    public void SnapshotMatchesRuntimeModelAndOnlyMapsOwnedTables()
    {
        using var db = new CompetenciesDbContext(new DbContextOptionsBuilder<CompetenciesDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=design_only;Username=design").Options);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        Assert.That(db.Model.GetEntityTypes().Select(x => x.GetSchema()).Distinct(), Is.EqualTo(new[] { "prg04" }));
        Assert.That(db.Model.GetEntityTypes().Select(x => x.GetTableName()),
            Is.EquivalentTo(new[] { "competencies", "laboratory_competencies" }));
        var migrator = db.GetService<IMigrator>();
        string up = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        string down = migrator.GenerateScript(db.Database.GetMigrations().Single(), "0");
        Assert.That(up, Does.Contain("CREATE TABLE prg04.laboratory_competencies"));
        Assert.That(down, Does.Contain("DROP TABLE prg04.laboratory_competencies"));
        Assert.That(up + down, Does.Not.Contain("authoring."));
    }

    [Test]
    public void RegistrationRequiresExplicitDatabaseAndUsesOneScopedStore()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCompetencyPersistence(" "));
        using var app = new ServiceCollection().AddCompetencyPersistence(
            "Host=127.0.0.1;Database=design_only;Username=design").BuildServiceProvider(validateScopes: true);
        using var firstScope = app.CreateScope();
        using var secondScope = app.CreateScope();
        var store = firstScope.ServiceProvider.GetRequiredService<ILaboratoryCompetencyStore>();
        Assert.That(firstScope.ServiceProvider.GetRequiredService<ICompetencyCatalog>(), Is.SameAs(store));
        Assert.That(secondScope.ServiceProvider.GetRequiredService<ILaboratoryCompetencyStore>(), Is.Not.SameAs(store));
    }
}
