using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Laboratories.Infrastructure.Composition;
using Npgsql;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Laboratories.IntegrationTests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public partial class PublicationDatabaseTests
{
    private const string ValidHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("publication_tests")
        .WithUsername("publication_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    private LaboratoriesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", LaboratoriesDbContext.SchemaName))
            .Options;
        return new LaboratoriesDbContext(options);
    }

    [Test]
    public async Task PersistenceRegistration_ResolvesContextAndConnectsToDatabase()
    {
        await using var provider = new ServiceCollection()
            .AddLaboratoriesPersistence(database.GetConnectionString())
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();

        Assert.That(await context.Database.CanConnectAsync(), Is.True);
        Assert.That(await context.Database.GetPendingMigrationsAsync(), Is.Empty);
    }
    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task Migrate_WhenRepeated_DoesNotDuplicateMigrationHistory()
    {
        await using var context = CreateContext();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await context.Database.MigrateAsync();

        var after = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(before, Has.Length.EqualTo(2));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(context.Database.HasPendingModelChanges(), Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_CanBeSavedAndLoadedInNewContext(bool requiresIndependentApproval)
    {
        var courseId = Guid.NewGuid();
        var creation = CoursePublishPolicy.Create(courseId, requiresIndependentApproval);
        Assert.That(creation, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        var policy = ((CoursePublishPolicy.CreationResult.Success)creation).Value;
        await using (var context = CreateContext())
        {
            context.CoursePublishPolicies.Add(policy);
            await context.SaveChangesAsync();
        }

        await using var reader = CreateContext();
        var saved = await reader.CoursePublishPolicies.SingleAsync(value => value.CourseId == courseId);
        Assert.Multiple(() =>
        {
            Assert.That(saved.CourseId, Is.EqualTo(courseId));
            Assert.That(saved.RequiresIndependentApproval, Is.EqualTo(requiresIndependentApproval));
        });
    }

    [TestCase(RevisionReviewDecision.Approved)]
    [TestCase(RevisionReviewDecision.ReturnedForChanges)]
    public async Task Decision_CanBeSavedAndLoadedInNewContext(RevisionReviewDecision decision)
    {
        var id = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(1));
        var creation = RevisionDecisionRecord.Create(id, revisionId, reviewerId, decision, time, ValidHash, true);
        Assert.That(creation, Is.TypeOf<RevisionDecisionRecord.CreationResult.Success>());
        var record = ((RevisionDecisionRecord.CreationResult.Success)creation).Value;
        await using (var context = CreateContext())
        {
            context.RevisionDecisions.Add(record);
            await context.SaveChangesAsync();
        }

        await using var reader = CreateContext();
        var saved = await reader.RevisionDecisions.SingleAsync(value => value.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(saved.RevisionId, Is.EqualTo(revisionId));
            Assert.That(saved.ReviewerId, Is.EqualTo(reviewerId));
            Assert.That(saved.Decision, Is.EqualTo(decision));
            Assert.That(saved.DecidedAt, Is.EqualTo(time));
            Assert.That(saved.DecidedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(saved.ContentHash, Is.EqualTo(ValidHash));
            Assert.That(saved.RequiresIndependentApproval, Is.True);
        });
    }

    [TestCase("decision", "CK_RevisionDecisions_Decision")]
    [TestCase("hash", "CK_RevisionDecisions_ContentHash")]
    [TestCase("revision", "CK_RevisionDecisions_RevisionId")]
    public async Task DirectSql_WithInvalidDecisionData_IsRejectedByDatabase(string invalidField, string constraint)
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        var revisionId = invalidField == "revision" ? Guid.Empty : Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var decision = invalidField == "decision" ? 99 : 1;
        var hash = invalidField == "hash" ? "invalid" : ValidHash;
        var time = DateTimeOffset.UtcNow;
        var error = Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO laboratories."RevisionDecisions"
                    ("Id", "RevisionId", "ReviewerId", "Decision", "DecidedAt", "ContentHash", "RequiresIndependentApproval")
                VALUES ({id}, {revisionId}, {reviewerId}, {decision}, {time}, {hash}, {true})
                """);
        });
        Assert.That(error, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(error!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
            Assert.That(error.ConstraintName, Is.EqualTo(constraint));
        });
        Assert.That(await context.RevisionDecisions.AnyAsync(value => value.Id == id), Is.False);
    }

    [Test]
    public async Task Policy_DuplicateCourse_IsRejectedByDatabase()
    {
        var courseId = Guid.NewGuid();
        var creation = CoursePublishPolicy.Create(courseId, true);
        Assert.That(creation, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        await using (var context = CreateContext())
        {
            context.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)creation).Value);
            await context.SaveChangesAsync();
        }

        await using var duplicateContext = CreateContext();
        var duplicateCreation = CoursePublishPolicy.Create(courseId, false);
        Assert.That(duplicateCreation, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
        duplicateContext.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)duplicateCreation).Value);
        var error = Assert.ThrowsAsync<DbUpdateException>(async () => { await duplicateContext.SaveChangesAsync(); });
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.InnerException, Is.TypeOf<PostgresException>());
        Assert.That(((PostgresException)error.InnerException!).SqlState, Is.EqualTo(PostgresErrorCodes.UniqueViolation));
    }
}
