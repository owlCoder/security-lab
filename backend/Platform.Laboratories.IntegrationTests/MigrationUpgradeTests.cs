using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Laboratories.IntegrationTests;

public partial class PublicationDatabaseTests
{
    [Test]
    public async Task UpgradeFromFirstMigration_PreservesPolicyAndDecisionHistory()
    {
        var databaseName = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", LaboratoriesDbContext.SchemaName))
            .Options;
        var courseId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();
        await using (var previous = new LaboratoriesDbContext(options))
        {
            var firstMigration = previous.Database.GetMigrations().First();
            await previous.GetService<IMigrator>().MigrateAsync(firstMigration);
            var policy = CoursePublishPolicy.Create(courseId, true);
            var record = RevisionDecisionRecord.Create(decisionId, Guid.NewGuid(), Guid.NewGuid(),
                RevisionReviewDecision.Approved, DateTimeOffset.UtcNow, ValidHash, true);
            Assert.That(policy, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
            Assert.That(record, Is.TypeOf<RevisionDecisionRecord.CreationResult.Success>());
            previous.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)policy).Value);
            previous.RevisionDecisions.Add(((RevisionDecisionRecord.CreationResult.Success)record).Value);
            await previous.SaveChangesAsync();
        }

        await using var upgraded = new LaboratoriesDbContext(options);
        await upgraded.Database.MigrateAsync();
        var savedPolicy = await upgraded.CoursePublishPolicies.SingleAsync(value => value.CourseId == courseId);
        var savedDecision = await upgraded.RevisionDecisions.SingleAsync(value => value.Id == decisionId);
        Assert.Multiple(() =>
        {
            Assert.That(savedPolicy.RequiresIndependentApproval, Is.True);
            Assert.That(savedDecision.ContentHash, Is.EqualTo(ValidHash));
            Assert.That(savedDecision.Decision, Is.EqualTo(RevisionReviewDecision.Approved));
        });
        Assert.That(await upgraded.LabRevisions.CountAsync(), Is.Zero);
        Assert.That(await upgraded.Database.GetPendingMigrationsAsync(), Is.Empty);
    }
}
