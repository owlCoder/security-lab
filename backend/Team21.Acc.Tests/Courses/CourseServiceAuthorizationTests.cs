using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Courses.Data;
using Team21.Acc.Infrastructure.Courses.Services;

namespace Team21.Acc.Tests.Courses;

/// <summary>
/// ACC-01 course creation and archiving: audit, caller authorization and input validation, through the
/// real <see cref="CourseService"/> on SQLite in memory.
/// <para>
/// Spec ACC-01: archiving stops new enrollments and keeps history. Spec 2.1 / AUD-01: privileged
/// actions are audited and a denial reveals nothing. SEC-01: authorization on the server for the
/// requested resource. Team 21 implementation: create denial is Forbidden and audited against
/// Guid.Empty, archive denial is NotFound "Course not found." and audited against the course id.
/// </para>
/// <para>
/// Seeding, the service call and the read-back each use their own context on one open connection,
/// so nothing the service tracked can hide what was really stored.
/// </para>
/// </summary>
public class CourseServiceAuthorizationTests
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<CoursesDbContext> _options = null!;
    private Guid _manager;
    private CourseAuthorization _authorization = null!;
    private RecordingAuditTrail _audit = null!;

    [SetUp]
    public async Task FreshDatabase()
    {
        // One in memory database per test; it lives as long as this connection is open
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<CoursesDbContext>().UseSqlite(_connection).Options;
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();

        _manager = Guid.NewGuid();
        _authorization = new CourseAuthorization();
        _audit = new RecordingAuditTrail();
    }

    [TearDown]
    public async Task CloseDatabase() => await _connection.DisposeAsync();

    // A01. Primary: ACC-01. Related: 2.1 / AUD-01 (privileged action audited).
    [Test]
    public async Task Create_AuthorizedUser_RecordsSuccessAudit()
    {
        _authorization.CanCreate = true;
        await using var serviceDb = NewDb();
        var service = CreateService(_manager, serviceDb);

        var result = await service.CreateAsync(new CreateCourseRequest("OIB-2027", "OIB", "Vežbe"));

        Assert.That(result.IsSuccess, Is.True);
        var created = result.Value!;
        var stored = await StoredCoursesAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(created.Id));
            Assert.That(stored[0].Code, Is.EqualTo("OIB-2027"));
            Assert.That(stored[0].Status, Is.EqualTo(CourseStatus.Active));
            Assert.That(stored[0].CreatedBy, Is.EqualTo(_manager));
        });
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("course.create", created.Id, AuditOutcome.Success)
        }));
    }

    // A02. Primary: ACC-01. Related: SEC-01, 2.1. Team 21: a denied create has no course yet, so it is
    // audited against Guid.Empty.
    [Test]
    public async Task Create_UserWithoutPermission_RecordsDeniedAudit()
    {
        _authorization.CanCreate = false;
        await using var serviceDb = NewDb();
        var service = CreateService(_manager, serviceDb);

        var result = await service.CreateAsync(new CreateCourseRequest("OIB-2027", "OIB", null));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Forbidden));
        Assert.That(await StoredCoursesAsync(), Is.Empty);
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("course.create", Guid.Empty, AuditOutcome.Denied)
        }));
    }

    // A03. Primary: ACC-01 (archiving keeps history). Related: 2.1 / AUD-01.
    [Test]
    public async Task Archive_AuthorizedUser_RecordsSuccessAudit()
    {
        var course = await SeedCourseAsync("OIB-2026");
        _authorization.ArchivableCourses.Add(course.Id);
        await using var serviceDb = NewDb();
        var service = CreateService(_manager, serviceDb);
        var before = DateTime.UtcNow;

        var result = await service.ArchiveAsync(course.Id);

        var after = DateTime.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        var stored = (await StoredCoursesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(CourseStatus.Archived));
            Assert.That(stored.CanEnroll, Is.False);
            Assert.That(stored.ArchivedBy, Is.EqualTo(_manager));
            Assert.That(stored.ArchivedAt, Is.InRange(before, after));
            Assert.That(stored.IsDeleted, Is.False, "archiving is not deleting");
            Assert.That(stored.Code, Is.EqualTo(course.Code));
            Assert.That(stored.Name, Is.EqualTo(course.Name));
            Assert.That(stored.Description, Is.EqualTo(course.Description));
            Assert.That(stored.CreatedAt, Is.EqualTo(course.CreatedAt));
            Assert.That(stored.CreatedBy, Is.EqualTo(course.CreatedBy));
        });
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("course.archive", course.Id, AuditOutcome.Success)
        }));
    }

    // A04. Primary: ACC-01. Related: SEC-01 (no caller, no change). The authorization double would allow
    // the archive, so only the missing caller can cause the denial.
    [Test]
    public async Task Archive_Anonymous_ReturnsUnauthorized()
    {
        var course = await SeedCourseAsync("OIB-2026");
        _authorization.ArchivableCourses.Add(course.Id);
        await using var serviceDb = NewDb();
        var service = CreateService(userId: null, serviceDb);

        var result = await service.ArchiveAsync(course.Id);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Unauthorized));
        AssertStillActiveAndUnchanged((await StoredCoursesAsync()).Single(), course);
        Assert.That(_audit.Records.Where(r => r.Outcome == AuditOutcome.Success), Is.Empty);
    }

    // A05. Primary: ACC-01. Related: SEC-01 (IDOR), 2.1 (denial reveals nothing, attempt audited).
    // The caller may archive course A only and asks to archive course B.
    [Test]
    public async Task Archive_CourseNotAuthorizedForCaller_ReturnsNotFound()
    {
        var courseA = await SeedCourseAsync("OIB-A");
        var courseB = await SeedCourseAsync("OIB-B");
        _authorization.ArchivableCourses.Add(courseA.Id);
        await using var serviceDb = NewDb();
        var service = CreateService(_manager, serviceDb);
        await using var missingDb = NewDb();
        var allowedEverywhere = new CourseService(
            missingDb, new TestActor(_manager), new CourseAuthorization { ArchiveAnything = true }, _audit);

        var result = await service.ArchiveAsync(courseB.Id);
        var missing = await allowedEverywhere.ArchiveAsync(Guid.NewGuid());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(result.Error, Is.EqualTo(missing.Error), "a denied course answers like a course that does not exist");
        var stored = await StoredCoursesAsync();
        AssertStillActiveAndUnchanged(stored.Single(c => c.Id == courseB.Id), courseB);
        AssertStillActiveAndUnchanged(stored.Single(c => c.Id == courseA.Id), courseA);
        // The missing course is not audited, so the only record is the denied attempt on B
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("course.archive", courseB.Id, AuditOutcome.Denied)
        }));
    }

    // A06. Primary: ACC-01. Related: SEC-05 (input validation). Limits come from Course; the empty name
    // case is already covered by CourseServiceTests.Create_EmptyName_ReturnsValidationError.
    [TestCaseSource(nameof(InvalidRequests))]
    public async Task Create_InvalidInput_ReturnsValidation(CreateCourseRequest request)
    {
        _authorization.CanCreate = true;
        await using var serviceDb = NewDb();
        var service = CreateService(_manager, serviceDb);

        var result = await service.CreateAsync(request);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(await StoredCoursesAsync(), Is.Empty);
    }

    private static IEnumerable<TestCaseData> InvalidRequests()
    {
        yield return new TestCaseData(new CreateCourseRequest("", "OIB", null))
            .SetArgDisplayNames("empty code");
        yield return new TestCaseData(new CreateCourseRequest(" \t ", "OIB", null))
            .SetArgDisplayNames("whitespace code");
        yield return new TestCaseData(new CreateCourseRequest(new string('C', Course.MaxCodeLength + 1), "OIB", null))
            .SetArgDisplayNames("code over limit");
        yield return new TestCaseData(new CreateCourseRequest("OIB", new string('n', Course.MaxNameLength + 1), null))
            .SetArgDisplayNames("name over limit");
        yield return new TestCaseData(new CreateCourseRequest("OIB", "OIB", new string('d', Course.MaxDescriptionLength + 1)))
            .SetArgDisplayNames("description over limit");
    }

    private CoursesDbContext NewDb() => new(_options);

    private CourseService CreateService(Guid? userId, CoursesDbContext db) =>
        new(db, new TestActor(userId), _authorization, _audit);

    private async Task<Course> SeedCourseAsync(string code)
    {
        await using var db = NewDb();
        var course = new Course(code, "Course " + code, "Istorija kursa", Guid.NewGuid());
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    // Read-back through a fresh context; soft deleted rows are included so a hidden delete shows up
    private async Task<List<Course>> StoredCoursesAsync()
    {
        await using var db = NewDb();
        return await db.Courses.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private static void AssertStillActiveAndUnchanged(Course stored, Course seeded)
    {
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(CourseStatus.Active));
            Assert.That(stored.CanEnroll, Is.True);
            Assert.That(stored.ArchivedAt, Is.Null);
            Assert.That(stored.ArchivedBy, Is.Null);
            Assert.That(stored.UpdatedAt, Is.Null);
            Assert.That(stored.IsDeleted, Is.False);
            Assert.That(stored.Code, Is.EqualTo(seeded.Code));
            Assert.That(stored.CreatedAt, Is.EqualTo(seeded.CreatedAt));
        });
    }

    // Test doubles: one small class per interface CourseService depends on

    private sealed record AuditRecord(string Action, Guid ResourceId, AuditOutcome Outcome);

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditRecord> Records { get; } = new();

        public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default)
        {
            Records.Add(new AuditRecord(action, resourceId, outcome));
            return Task.CompletedTask;
        }
    }

    private sealed class TestActor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId { get; } = userId;
        public bool IsAuthenticated => UserId is not null;
    }

    // Archive permission is decided per course, so a check against the wrong course id would show up.
    // Viewing is not used by these tests and is denied.
    private sealed class CourseAuthorization : ICourseAuthorization
    {
        public bool CanCreate { get; set; }
        public bool ArchiveAnything { get; init; }
        public HashSet<Guid> ArchivableCourses { get; } = new();

        public Task<bool> CanCreateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanCreate);

        public Task<bool> CanArchiveAsync(Guid userId, Guid courseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ArchiveAnything || ArchivableCourses.Contains(courseId));

        public Task<bool> CanViewAsync(Guid userId, Guid courseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
