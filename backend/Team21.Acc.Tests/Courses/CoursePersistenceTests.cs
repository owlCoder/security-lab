using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Courses.Configurations;
using Team21.Acc.Infrastructure.Courses.Data;

namespace Team21.Acc.Tests.Courses;

public class CoursePersistenceTests
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<CoursesDbContext> _options = null!;

    [SetUp]
    public async Task CreateDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<CoursesDbContext>().UseSqlite(_connection).Options;
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task CloseDatabase() => await _connection.DisposeAsync();

    [Test]
    public async Task ArchiveRoundTripKeepsCourseVisibleAndCreationMetadataUnchanged()
    {
        var original = await InsertCourse();
        var actor = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var course = await db.Courses.SingleAsync();
            course.Archive(actor);
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        var stored = await check.Courses.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Id, Is.EqualTo(original.Id));
            Assert.That(stored.Code, Is.EqualTo(original.Code));
            Assert.That(stored.Name, Is.EqualTo(original.Name));
            Assert.That(stored.Description, Is.EqualTo(original.Description));
            Assert.That(stored.CreatedAt, Is.EqualTo(original.CreatedAt));
            Assert.That(stored.CreatedBy, Is.EqualTo(original.CreatedBy));
            Assert.That(stored.Status, Is.EqualTo(CourseStatus.Archived));
            Assert.That(stored.CanEnroll, Is.False);
            Assert.That(stored.IsDeleted, Is.False);
            Assert.That(stored.ArchivedBy, Is.EqualTo(actor));
            Assert.That(stored.ArchivedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(stored.UpdatedAt, Is.Not.Null);
        });
        stored.Archive(Guid.NewGuid());
        Assert.That(await check.SaveChangesAsync(), Is.Zero);
        Assert.That(await check.Courses.IgnoreQueryFilters().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task StaleArchiveCannotOverwriteFirstArchiveActor()
    {
        await InsertCourse();
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Courses.SingleAsync();
        var b = await second.Courses.SingleAsync();
        var firstActor = Guid.NewGuid();
        a.Archive(firstActor);
        await first.SaveChangesAsync();
        b.Archive(Guid.NewGuid());

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await second.SaveChangesAsync());
        await using var check = NewContext();
        Assert.That((await check.Courses.SingleAsync()).ArchivedBy, Is.EqualTo(firstActor));
    }

    [TestCase("Code", "")]
    [TestCase("Code", "   ")]
    [TestCase("Code", null)]
    [TestCase("Code", "lowercase")]
    [TestCase("Name", "")]
    [TestCase("Name", "   ")]
    [TestCase("Name", null)]
    [TestCase("Description", null)]
    [TestCase("Status", null)]
    [TestCase("Status", "Unknown")]
    [TestCase("Status", "Archived")]
    [TestCase("CreatedBy", "00000000-0000-0000-0000-000000000000")]
    [TestCase("ArchivedBy", "00000000-0000-0000-0000-000000000001")]
    public async Task DatabaseRejectsInvalidDataEvenWhenDomainIsBypassed(string column, string? value)
    {
        await InsertCourse();
        await using var db = NewContext();
        var parameter = new SqliteParameter("value", SqliteType.Text) { Value = (object?)value ?? DBNull.Value };
        var error = Assert.ThrowsAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlRawAsync(UpdateColumnSql(column), parameter));
        Assert.That(error!.SqliteErrorCode, Is.EqualTo(19)); // SQLITE_CONSTRAINT
    }

    [TestCase("Code", Course.MaxCodeLength)]
    [TestCase("Name", Course.MaxNameLength)]
    [TestCase("Description", Course.MaxDescriptionLength)]
    public async Task DatabaseRejectsOverlongText(string column, int limit)
    {
        await InsertCourse();
        await using var db = NewContext();
        var error = Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(
            UpdateColumnSql(column), new string('X', limit + 1)));
        Assert.That(error!.SqliteErrorCode, Is.EqualTo(19));
    }

    [Test]
    public async Task ArchivedCourseMustKeepItsArchiveMetadata()
    {
        var course = await InsertCourse();
        await using var db = NewContext();
        db.Attach(course);
        course.Archive(Guid.NewGuid());
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Courses\" SET \"ArchivedAt\" = NULL"));
        Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Courses\" SET \"ArchivedBy\" = '00000000-0000-0000-0000-000000000000'"));
    }

    [Test]
    public async Task SameNameIsAllowedButDuplicateIdentityIsRejected()
    {
        var original = await InsertCourse();
        await using var db = NewContext();
        db.Courses.Add(new Course("OIB-OTHER", original.Name, null, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.Courses.Add(new Course("OTHER", "Other", null, Guid.NewGuid()) { Id = original.Id });
        Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Test]
    public async Task ArchivedCourseStillReservesItsCode()
    {
        var original = await InsertCourse();
        await using var db = NewContext();
        db.Attach(original);
        original.Archive(Guid.NewGuid());
        await db.SaveChangesAsync();
        db.ClearChangeTracker();
        db.Courses.Add(new Course(original.Code.ToLowerInvariant(), "Other", null, Guid.NewGuid()));
        Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Test]
    public async Task SampleDataIsRepeatableAndNeverOverwritesExistingOrDeletedCourses()
    {
        await using var db = NewContext();
        await CourseDevelopmentSeed.SeedAsync(db, "Testing");
        var sample = await db.Courses.FirstAsync(course => course.Status == CourseStatus.Active);
        sample.Archive(Guid.NewGuid());
        sample.IsDeleted = true;
        await db.SaveChangesAsync();
        var archivedBy = sample.ArchivedBy;
        db.ClearChangeTracker();

        await CourseDevelopmentSeed.SeedAsync(db, "Testing");
        Assert.That(await db.Courses.IgnoreQueryFilters().CountAsync(), Is.EqualTo(3));
        Assert.That(await db.Courses.CountAsync(), Is.EqualTo(2));
        var kept = await db.Courses.IgnoreQueryFilters().SingleAsync(course => course.Id == sample.Id);
        Assert.That(kept.ArchivedBy, Is.EqualTo(archivedBy));
        Assert.That(kept.IsDeleted, Is.True);
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    [TestCase("")]
    public async Task SampleDataIsRejectedOutsideDevelopmentAndTesting(string environment)
    {
        await using var db = NewContext();
        Assert.ThrowsAsync<InvalidOperationException>(() => CourseDevelopmentSeed.SeedAsync(db, environment));
        Assert.That(await db.Courses.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ArchivePreservesRelatedHistoryAndRestrictForeignKeyProtectsIt()
    {
        // A test-only record stands in for a future history entity, not an implementation of ACC-02/ATT.
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var historyOptions = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(connection).Options;
        var course = new Course("OIB", "OIB", "Istorijski rezultati", Guid.NewGuid());
        var historyId = Guid.NewGuid();
        await using (var seed = new HistoryContext(historyOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Courses.Add(course);
            seed.History.Add(new HistoryRecord { Id = historyId, CourseId = course.Id, Payload = "Score=8;Revision=1" });
            await seed.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<CoursesDbContext>().UseSqlite(connection).Options;
        await using (var db = new CoursesDbContext(options))
        {
            var loaded = await db.Courses.SingleAsync();
            loaded.Archive(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using var check = new HistoryContext(historyOptions);
        var history = await check.History.SingleAsync();
        Assert.That((history.Id, history.CourseId, history.Payload),
            Is.EqualTo((historyId, course.Id, "Score=8;Revision=1")));
        Assert.That((await check.Courses.SingleAsync()).CanEnroll, Is.False);
        Assert.ThrowsAsync<SqliteException>(async () =>
            await check.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Courses\" WHERE \"Id\" = {course.Id}"));
        Assert.That(await check.History.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public void PostgreSqlMigrationMatchesModelAndOnlyAddsCourses()
    {
        var options = new DbContextOptionsBuilder<CoursesDbContext>();
        options.UseTeamNpgsql("Host=localhost;Database=unused;Username=unused", CoursesDbContext.SchemaName);
        using var db = new CoursesDbContext(options.Options);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        var sql = db.GetService<IMigrator>().GenerateScript();
        Assert.That(sql, Does.Contain("CREATE TABLE courses.\"Courses\""));
        Assert.That(sql, Does.Contain("CK_Courses_ArchiveMetadata"));
        Assert.That(sql, Does.Contain("CREATE INDEX"));
        Assert.That(sql, Does.Not.Contain("DROP TABLE").And.Not.Contain("DELETE FROM"));
        Assert.That(db.Model.GetEntityTypes().Select(type => type.ClrType), Is.EqualTo(new[] { typeof(Course) }));
    }

    private CoursesDbContext NewContext() => new(_options);

    private async Task<Course> InsertCourse()
    {
        await using var db = NewContext();
        var course = new Course("OIB", "OIB", "Sačuvani podaci", Guid.NewGuid());
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    private static string UpdateColumnSql(string column) => column switch
    {
        "Code" => "UPDATE \"Courses\" SET \"Code\" = {0}",
        "Name" => "UPDATE \"Courses\" SET \"Name\" = {0}",
        "Description" => "UPDATE \"Courses\" SET \"Description\" = {0}",
        "Status" => "UPDATE \"Courses\" SET \"Status\" = {0}",
        "CreatedBy" => "UPDATE \"Courses\" SET \"CreatedBy\" = {0}",
        "ArchivedBy" => "UPDATE \"Courses\" SET \"ArchivedBy\" = {0}",
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    private sealed class HistoryRecord
    {
        public Guid Id { get; set; }
        public Guid CourseId { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : TeamDbContextBase(options)
    {
        protected override string Schema => CoursesDbContext.SchemaName;
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<HistoryRecord> History => Set<HistoryRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new CourseConfiguration());
            modelBuilder.Entity<HistoryRecord>().HasOne<Course>().WithMany()
                .HasForeignKey(record => record.CourseId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
