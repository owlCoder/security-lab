using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Courses;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Courses.Data;
using Team21.Acc.Infrastructure.Courses.Services;

namespace Team21.Acc.Tests.Courses;

public class CourseServiceTests
{
    private SqliteConnection _connection = null!;
    private CoursesDbContext _db = null!;

    private static readonly Guid ManagerId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoursesDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoursesDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
[Test]
public async Task GetCourse_AllowedUser_ReturnsCourse()
{
    // Arrange
    var course = new Course(
        "GET-TEST",
        "Networking Fundamentals",
        "Introduction to networking",
        ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(ManagerId, true);

    // Act
    var result = await service.GetCourseAsync(course.Id);

    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value, Is.Not.Null);

    Assert.Multiple(() =>
    {
        Assert.That(result.Value!.Id, Is.EqualTo(course.Id));
        Assert.That(result.Value.Code, Is.EqualTo("GET-TEST"));
        Assert.That(result.Value.Name,
            Is.EqualTo("Networking Fundamentals"));
        Assert.That(result.Value.Status, Is.EqualTo("Active"));
    });
}


[Test]
public async Task GetCourses_ReturnsSortedCoursesIncludingArchived()
{
    // Arrange
    var courseB = new Course(
        "B-COURSE", "Second Course", null, ManagerId);

    var courseA = new Course(
        "A-COURSE", "First Course", null, ManagerId);

    courseB.Archive(ManagerId);

    _db.Courses.AddRange(courseB, courseA);
    await _db.SaveChangesAsync();

    var service = CreateService(ManagerId, true);

    // Act
    var result = await service.GetCoursesAsync();


    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value, Is.Not.Null);

    var courses = result.Value!;

    Assert.That(courses, Has.Count.EqualTo(2));

    Assert.Multiple(() =>
    {
        Assert.That(
            courses.Select(c => c.Code).ToArray(),
            Is.EqualTo(new[] { "A-COURSE", "B-COURSE" }));

        Assert.That(
            courses.Single(c => c.Code == "B-COURSE").Status,
            Is.EqualTo("Archived"));
    });

}



[Test]
public async Task GetCourse_AnonymousUser_ReturnsUnauthorized()
{
    var course = new Course(
        "PRIVATE-101", "Private Course", null, ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(null, true);

    var result = await service.GetCourseAsync(course.Id);

    Assert.That(result.Error?.Type,
        Is.EqualTo(ErrorType.Unauthorized));
}


[Test]
public async Task GetCourses_AnonymousUser_ReturnsUnauthorized()
{
    var service = CreateService(null, true);

    var result = await service.GetCoursesAsync();

    Assert.That(result.Error?.Type,
        Is.EqualTo(ErrorType.Unauthorized));
}


[Test]
public async Task GetCourse_NonExistingCourse_ReturnsNotFound()
{
    var service = CreateService(ManagerId, true);

    var result = await service.GetCourseAsync(Guid.NewGuid());

    Assert.That(result.Error?.Type,
        Is.EqualTo(ErrorType.NotFound));
}


[Test]
public async Task GetCourse_UserWithoutPermission_ReturnsNotFound()
{
    var course = new Course(
        "SECRET-101", "Protected Course", null, ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(Guid.NewGuid(), false);

    var result = await service.GetCourseAsync(course.Id);

    Assert.That(result.Error?.Type,
        Is.EqualTo(ErrorType.NotFound));
}


[Test]
public async Task GetCourses_UserWithoutPermission_ReturnsEmptyList()
{
    var course = new Course(
        "HIDDEN-101", "Hidden Course", null, ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(Guid.NewGuid(), false);

    var result = await service.GetCoursesAsync();

    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value, Is.Not.Null);
    Assert.That(result.Value, Is.Empty);
}


[Test]
public async Task GetCourse_ArchivedCourse_IsStillReadable()
{
    // Arrange
    var course = new Course(
        "ARCHIVED-GET",
        "Archived Networking Course",
        null,
        ManagerId);

    course.Archive(ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(ManagerId, true);

    // Act
    var result = await service.GetCourseAsync(course.Id);

    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value, Is.Not.Null);

    var dto = result.Value!;

    Assert.Multiple(() =>
    {
        Assert.That(dto.Id, Is.EqualTo(course.Id));
        Assert.That(dto.Status, Is.EqualTo("Archived"));
        Assert.That(dto.ArchivedAt, Is.Not.Null);
    });
}


[Test]
public async Task GetCourses_OnlyReturnsAuthorizedCourses()
{
    // Arrange
    var allowedCourse = new Course(
        "ALLOWED-101", "Allowed Course", null, ManagerId);

    var hiddenCourse = new Course(
        "HIDDEN-102", "Hidden Course", null, ManagerId);

    _db.Courses.AddRange(allowedCourse, hiddenCourse);
    await _db.SaveChangesAsync();

    // User may view only allowedCourse.
    var service = CreateService(
        ManagerId,
        true,
        allowedCourse.Id);

    // Act
    var listResult = await service.GetCoursesAsync();
    var allowedResult = await service.GetCourseAsync(allowedCourse.Id);
    var hiddenResult = await service.GetCourseAsync(hiddenCourse.Id);

    // Assert
    Assert.That(listResult.IsSuccess, Is.True);
    Assert.That(listResult.Value, Is.Not.Null);

    var courses = listResult.Value!;

    Assert.Multiple(() =>
    {
        Assert.That(courses, Has.Count.EqualTo(1));
        Assert.That(courses[0].Id, Is.EqualTo(allowedCourse.Id));

        Assert.That(allowedResult.IsSuccess, Is.True);

        Assert.That(
            hiddenResult.Error?.Type,
            Is.EqualTo(ErrorType.NotFound));
    });
}



private CourseService CreateService(
    Guid? actorId,
    bool allowed,
    Guid? visibleCourseId = null)
{
    return new CourseService(
        _db,
        new TestActor(actorId),
        new TestAuthorization(allowed, visibleCourseId),
        new TestAuditTrail());
}


    [Test]
    public async Task Create_ValidRequest_CreatesCourse()
    {
        var service = CreateService(ManagerId, true);

        var result = await service.CreateAsync(
            new CreateCourseRequest(
                " oib-test ",
                "Test Course",
                "Description"));

        Assert.That(result.IsSuccess, Is.True);

        var saved = await _db.Courses.AsNoTracking().SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Code, Is.EqualTo("OIB-TEST"));
            Assert.That(saved.Name, Is.EqualTo("Test Course"));
            Assert.That(saved.Status, Is.EqualTo(CourseStatus.Active));
            Assert.That(saved.CreatedBy, Is.EqualTo(ManagerId));
        });
    }

    [Test]
    public async Task Create_AnonymousUser_ReturnsUnauthorized()
    {
        var service = CreateService(null, true);

        var result = await service.CreateAsync(
            new CreateCourseRequest("TEST", "Test Course", null));

        Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.Unauthorized));
        Assert.That(await _db.Courses.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_UserWithoutPermission_ReturnsForbidden()
    {
        var service = CreateService(Guid.NewGuid(), false);

        var result = await service.CreateAsync(
            new CreateCourseRequest("TEST", "Test Course", null));

        Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.Forbidden));
        Assert.That(await _db.Courses.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_EmptyName_ReturnsValidationError()
    {
        var service = CreateService(ManagerId, true);

        var result = await service.CreateAsync(
            new CreateCourseRequest("TEST", "", null));

        Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(await _db.Courses.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        _db.Courses.Add(
            new Course("TEST", "Original Course", null, ManagerId));

        await _db.SaveChangesAsync();

        var service = CreateService(ManagerId, true);

        var result = await service.CreateAsync(
            new CreateCourseRequest("test", "Duplicate Course", null));

        Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.Conflict));
        Assert.That(await _db.Courses.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Archive_ValidRequest_ArchivesCourse()
    {
        var course = new Course(
            "ARCHIVE-TEST",
            "Archive Test",
            null,
            ManagerId);

        _db.Courses.Add(course);
        await _db.SaveChangesAsync();

        var service = CreateService(ManagerId, true);

        var result = await service.ArchiveAsync(course.Id);

        Assert.That(result.IsSuccess, Is.True);

        _db.ChangeTracker.Clear();

        var saved = await _db.Courses.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Status, Is.EqualTo(CourseStatus.Archived));
            Assert.That(saved.ArchivedAt, Is.Not.Null);
            Assert.That(saved.ArchivedBy, Is.EqualTo(ManagerId));
            Assert.That(saved.IsDeleted, Is.False);
        });
    }

    [Test]
public async Task Archive_UserWithoutPermission_ReturnsNotFound()
{
    var course = new Course(
        "ARCHIVE-DENIED",
        "Protected Course",
        null,
        ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(Guid.NewGuid(), false);

    var result = await service.ArchiveAsync(course.Id);

    Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.NotFound));

    _db.ChangeTracker.Clear();

    var saved = await _db.Courses.SingleAsync();

    Assert.Multiple(() =>
    {
        Assert.That(saved.Status, Is.EqualTo(CourseStatus.Active));
        Assert.That(saved.ArchivedAt, Is.Null);
        Assert.That(saved.ArchivedBy, Is.Null);
    });
}

    private sealed class TestAuditTrail : IAuditTrail
    {
        public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class TestActor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId => userId;

        public bool IsAuthenticated =>
            userId.HasValue && userId.Value != Guid.Empty;
    }

    private sealed class TestAuthorization(
    bool allowed,
    Guid? visibleCourseId = null)
    : ICourseAuthorization
    {
        public Task<bool> CanCreateAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(allowed);
        }

        public Task<bool> CanArchiveAsync(
            Guid userId,
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(allowed);
        }

public Task<bool> CanViewAsync(
    Guid userId,
    Guid courseId,
    CancellationToken cancellationToken = default)
{
    return Task.FromResult(
        allowed &&
        (visibleCourseId is null || visibleCourseId == courseId));
}


    }

    [Test]
public async Task Archive_NonExistingCourse_ReturnsNotFound()
{
    var service = CreateService(ManagerId, true);

    var result = await service.ArchiveAsync(Guid.NewGuid());

    Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.NotFound));
}

[Test]
public async Task Archive_Twice_IsIdempotent()
{
    var course = new Course(
        "ARCHIVE-TWICE",
        "Idempotency Test",
        null,
        ManagerId);

    _db.Courses.Add(course);
    await _db.SaveChangesAsync();

    var service = CreateService(ManagerId, true);

    // First archive
    var firstResult = await service.ArchiveAsync(course.Id);

    Assert.That(firstResult.IsSuccess, Is.True);

    _db.ChangeTracker.Clear();

    var firstSaved = await _db.Courses.SingleAsync();

    var originalArchivedAt = firstSaved.ArchivedAt;
    var originalArchivedBy = firstSaved.ArchivedBy;

    // Second archive
    var secondResult = await service.ArchiveAsync(course.Id);

    Assert.That(secondResult.IsSuccess, Is.True);

    _db.ChangeTracker.Clear();

    var secondSaved = await _db.Courses.SingleAsync();

    Assert.Multiple(() =>
    {
        Assert.That(secondSaved.Status,
            Is.EqualTo(CourseStatus.Archived));

        Assert.That(secondSaved.ArchivedAt,
            Is.EqualTo(originalArchivedAt));

        Assert.That(secondSaved.ArchivedBy,
            Is.EqualTo(originalArchivedBy));

        Assert.That(secondSaved.IsDeleted, Is.False);
    });

    Assert.That(await _db.Courses.CountAsync(), Is.EqualTo(1));
}
}