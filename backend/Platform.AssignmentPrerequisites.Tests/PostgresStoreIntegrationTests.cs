using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.AssignmentPrerequisites.Domain;
using Platform.AssignmentPrerequisites.Infrastructure.Persistence;
using static Platform.AssignmentPrerequisites.Tests.Support.TestData;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PostgresStoreIntegrationTests
{
    private string _connectionString = null!;

    private string _course = null!;

    [OneTimeSetUp]
    public async Task MigrateDatabase()
    {
        string? connectionString = LocalConnectionString.Find(TestContext.CurrentContext.TestDirectory);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("No local connection string; PostgreSQL integration tests skipped.");
            return;
        }

        _connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 3 }.ConnectionString;

        await using AssignmentPrerequisitesDbContext db = CreateContext();
        if (!await db.Database.CanConnectAsync())
        {
            Assert.Ignore("PostgreSQL is not running; start it with 'docker compose up -d db'.");
            return;
        }

        await db.Database.MigrateAsync();
    }

    [SetUp]
    public void NewCourse() => _course = "it-" + Guid.NewGuid().ToString("N")[..20];

    [TearDown]
    public async Task DeleteTestRows()
    {
        await using AssignmentPrerequisitesDbContext db = CreateContext();
        await db.Prerequisites.Where(p => p.CourseId == _course).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Prerequisite_SurvivesNewContext()
    {
        Guid x = Guid.NewGuid();
        Guid y = Guid.NewGuid();

        await using (AssignmentPrerequisitesDbContext db = CreateContext())
        {
            bool added = await new PostgresAssignmentPrerequisiteStore(db)
                .AddAsync(Requires(x, y, CompletionRequirement.PassedOrPartial, _course));
            Assert.That(added, Is.True);
        }

        await using (AssignmentPrerequisitesDbContext db = CreateContext())
        {
            IReadOnlyList<AssignmentPrerequisite> stored =
                await new PostgresAssignmentPrerequisiteStore(db).GetForAssignmentAsync(x);

            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(stored[0].RequiredAssignmentId, Is.EqualTo(y));
                Assert.That(stored[0].CourseId, Is.EqualTo(_course));
                Assert.That(stored[0].CompletionRequirement, Is.EqualTo(CompletionRequirement.PassedOrPartial));
                Assert.That(stored[0].CreatedById, Is.EqualTo(Teacher));
                Assert.That(stored[0].CreatedAtUtc, Is.EqualTo(Now));
            });
        }
    }

    [Test]
    public async Task Duplicate_IsRejectedByPrimaryKey()
    {
        Guid x = Guid.NewGuid();
        Guid y = Guid.NewGuid();

        await using AssignmentPrerequisitesDbContext db = CreateContext();
        var store = new PostgresAssignmentPrerequisiteStore(db);

        Assert.That(await store.AddAsync(Requires(x, y, course: _course)), Is.True);
        Assert.That(await store.AddAsync(Requires(x, y, course: _course)), Is.False);
        Assert.That(await store.GetForCourseAsync(_course), Has.Count.EqualTo(1));
    }

    [Test]
    public void SelfReference_IsRejectedByDatabase()
    {
        Guid x = Guid.NewGuid();

        PostgresException? error = Assert.ThrowsAsync<PostgresException>(() => InsertRawAsync(x, x, 1));

        Assert.That(error!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        Assert.That(error.ConstraintName, Is.EqualTo("ck_prerequisites_not_self"));
    }

    [Test]
    public void UnknownCompletionRequirement_IsRejectedByDatabase()
    {
        PostgresException? error = Assert.ThrowsAsync<PostgresException>(
            () => InsertRawAsync(Guid.NewGuid(), Guid.NewGuid(), 99));

        Assert.That(error!.ConstraintName, Is.EqualTo("ck_prerequisites_completion_requirement"));
    }

    [Test]
    public async Task Remove_DeletesRow()
    {
        Guid x = Guid.NewGuid();
        Guid y = Guid.NewGuid();

        await using AssignmentPrerequisitesDbContext db = CreateContext();
        var store = new PostgresAssignmentPrerequisiteStore(db);
        await store.AddAsync(Requires(x, y, course: _course));

        Assert.That(await store.RemoveAsync(x, y), Is.True);
        Assert.That(await store.RemoveAsync(x, y), Is.False);
        Assert.That(await store.GetForAssignmentAsync(x), Is.Empty);
    }

    private async Task InsertRawAsync(Guid assignmentId, Guid requiredAssignmentId, short requirement)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "INSERT INTO assignment_prerequisites.prerequisites " +
            "(assignment_id, required_assignment_id, course_id, completion_requirement, created_by_id, created_at_utc) " +
            "VALUES (@x, @y, @course, @requirement, @creator, now())",
            connection);
        command.Parameters.AddWithValue("x", assignmentId);
        command.Parameters.AddWithValue("y", requiredAssignmentId);
        command.Parameters.AddWithValue("course", _course);
        command.Parameters.AddWithValue("requirement", requirement);
        command.Parameters.AddWithValue("creator", Teacher);

        await command.ExecuteNonQueryAsync();
    }

    private AssignmentPrerequisitesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssignmentPrerequisitesDbContext>();
        options.UseAssignmentPrerequisitesNpgsql(_connectionString);
        return new AssignmentPrerequisitesDbContext(options.Options);
    }
}
