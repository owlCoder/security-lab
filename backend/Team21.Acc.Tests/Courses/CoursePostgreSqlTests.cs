using Microsoft.EntityFrameworkCore;
using Npgsql;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Tests.Courses;

public class CoursePostgreSqlTests
{
    [Test]
    [Category("PostgreSql")]
    public async Task MigrationArchiveAndConstraintsWorkOnPostgreSql()
    {
        // Opt-in only: use a dedicated test database. Migrations remain; test rows are rolled back.
        var connectionString = Environment.GetEnvironmentVariable("TEAM21_ACC_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Set TEAM21_ACC_TEST_POSTGRES to a dedicated PostgreSQL test database.");
            return;
        }

        var options = new DbContextOptionsBuilder<CoursesDbContext>();
        options.UseTeamNpgsql(connectionString, CoursesDbContext.SchemaName);
        await using var db = new CoursesDbContext(options.Options);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var course = new Course(Guid.NewGuid().ToString("N"), "PostgreSQL OIB test", "History must survive", Guid.NewGuid());
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var createdAt = course.CreatedAt;
        var actor = Guid.NewGuid();
        course.Archive(actor);
        await db.SaveChangesAsync();
        db.ClearChangeTracker();

        var stored = await db.Courses.SingleAsync(item => item.Id == course.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.CanEnroll, Is.False);
            Assert.That(stored.IsDeleted, Is.False);
            Assert.That(stored.CreatedAt, Is.EqualTo(createdAt).Within(TimeSpan.FromMicroseconds(1)));
            Assert.That(stored.ArchivedBy, Is.EqualTo(actor));
            Assert.That(stored.ArchivedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });

        var error = Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE courses.\"Courses\" SET \"ArchivedAt\" = NULL WHERE \"Id\" = {course.Id}"));
        Assert.That(error!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
        // Dispose rolls back the test transaction, including after the rejected statement.
    }
}
