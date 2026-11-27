using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Data;
using Team21.Acc.Infrastructure.Courses.Services;

namespace Team21.Acc.Tests.Courses;

/// <summary>ACC-01 acceptance: archiving stops new enrollments and does not delete history.</summary>
public class CourseEnrollmentPolicyTests
{
    private static readonly Guid ManagerId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private SqliteConnection _connection = null!;
    private DbContextOptions<CoursesDbContext> _options = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<CoursesDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var db = new CoursesDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task Check_ActiveCourse_IsOpen()
    {
        var course = await AddCourseAsync("OPEN-101");

        var decision = await CheckAsync(course.Id);

        Assert.That(decision, Is.EqualTo(CourseEnrollmentDecision.Open));
    }

    [Test]
    public async Task Check_ArchivedCourse_IsArchived()
    {
        var course = await AddCourseAsync("ARCH-101", archive: true);

        var decision = await CheckAsync(course.Id);

        Assert.That(decision, Is.EqualTo(CourseEnrollmentDecision.Archived));
    }

    [Test]
    public async Task Check_UnknownCourse_IsNotFound()
    {
        var decision = await CheckAsync(Guid.NewGuid());

        Assert.That(decision, Is.EqualTo(CourseEnrollmentDecision.NotFound));
    }

    [Test]
    public async Task Check_EmptyId_IsNotFound()
    {
        var decision = await CheckAsync(Guid.Empty);

        Assert.That(decision, Is.EqualTo(CourseEnrollmentDecision.NotFound));
    }

    [Test]
    public async Task Check_SoftDeletedCourse_IsNotFound()
    {
        var course = await AddCourseAsync("GONE-101");

        await using (var db = new CoursesDbContext(_options))
        {
            var stored = await db.Courses.SingleAsync(c => c.Id == course.Id);
            stored.IsDeleted = true;
            stored.DeletedAt = DateTime.UtcNow;
            stored.DeletedBy = ManagerId;
            await db.SaveChangesAsync();
        }

        var decision = await CheckAsync(course.Id);

        Assert.That(decision, Is.EqualTo(CourseEnrollmentDecision.NotFound));
    }

    [Test]
    public async Task Check_AfterArchiveThroughService_NextCheckIsArchived()
    {
        var course = await AddCourseAsync("LIVE-101");
        Assert.That(await CheckAsync(course.Id), Is.EqualTo(CourseEnrollmentDecision.Open));

        await using (var db = new CoursesDbContext(_options))
        {
            var service = new CourseService(
                db,
                new FixedActor(ManagerId),
                new AllowAllAuthorization(),
                new NoAuditTrail());

            var archived = await service.ArchiveAsync(course.Id);
            Assert.That(archived.IsSuccess, Is.True);
        }

        // A fresh context, as the next request would have.
        Assert.That(await CheckAsync(course.Id), Is.EqualTo(CourseEnrollmentDecision.Archived));
    }

    [Test]
    public async Task Check_ArchivedCourse_KeepsHistory()
    {
        var course = await AddCourseAsync("HIST-101", archive: true);

        await CheckAsync(course.Id);

        await using var db = new CoursesDbContext(_options);
        var stored = await db.Courses
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == course.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.IsDeleted, Is.False);
            Assert.That(stored.Status, Is.EqualTo(CourseStatus.Archived));
            Assert.That(stored.ArchivedBy, Is.EqualTo(ManagerId));
            Assert.That(stored.Name, Is.EqualTo("Course HIST-101"));
        });
    }

    private async Task<Course> AddCourseAsync(string code, bool archive = false)
    {
        await using var db = new CoursesDbContext(_options);
        var course = new Course(code, $"Course {code}", null, ManagerId);
        if (archive)
            course.Archive(ManagerId);

        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    private async Task<CourseEnrollmentDecision> CheckAsync(Guid courseId)
    {
        await using var db = new CoursesDbContext(_options);
        return await new CourseEnrollmentPolicy(db).CheckAsync(courseId);
    }

    private sealed class FixedActor(Guid userId) : Core.Interfaces.ICurrentActor
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class AllowAllAuthorization : ICourseAuthorization
    {
        public Task<bool> CanCreateAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CanArchiveAsync(Guid userId, Guid courseId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CanViewAsync(Guid userId, Guid courseId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class NoAuditTrail : Core.Interfaces.IAuditTrail
    {
        public Task RecordAsync(string action, Guid resourceId, Core.Interfaces.AuditOutcome outcome,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
