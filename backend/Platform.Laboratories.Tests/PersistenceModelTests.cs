using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Laboratories.Tests;

public class PersistenceModelTests
{
    private static LaboratoriesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql("Host=localhost;Database=model_validation_only", postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", LaboratoriesDbContext.SchemaName))
            .Options;
        return new LaboratoriesDbContext(options);
    }

    [Test]
    public void Model_MapsReadOnlyPolicyAndRevisionConcurrencyToken()
    {
        using var context = CreateContext();
        var policy = context.Model.FindEntityType(typeof(CoursePublishPolicy));
        Assert.That(policy, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(policy!.FindProperty(nameof(CoursePublishPolicy.CourseId)), Is.Not.Null);
            Assert.That(policy.FindProperty(nameof(CoursePublishPolicy.RequiresIndependentApproval)), Is.Not.Null);
            Assert.That(context.Model.FindEntityType(typeof(LabRevision))?.FindProperty(nameof(LabRevision.RowVersion))?.IsConcurrencyToken, Is.True);
        });
    }

    [Test]
    public void GenerateCreateScript_IncludesPostgresConstraintsAndSeparateSchema()
    {
        using var context = CreateContext();

        var sql = context.Database.GenerateCreateScript();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("laboratories.\"CoursePublishPolicies\""));
            Assert.That(sql, Does.Contain("laboratories.\"RevisionDecisions\""));
            Assert.That(sql, Does.Contain("CK_RevisionDecisions_ContentHash"));
            Assert.That(sql, Does.Contain("^[0-9a-f]{64}$"));
            Assert.That(sql, Does.Contain("CK_RevisionDecisions_Decision"));
            Assert.That(sql, Does.Contain("timestamp with time zone"));
            Assert.That(sql, Does.Not.Contain("FOREIGN KEY"));
        });
    }
}
