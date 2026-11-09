using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

namespace Laboratories.Tests;

// Jedinicni testovi LAB-01 i LAB-03 slucajeva upotrebe, bez HTTP-a.
[TestFixture]
public sealed class LaboratoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCourseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private InMemoryLaboratoryRepository _repository = null!;
    private InMemoryCourseMembership _membership = null!;
    private LaboratoryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryLaboratoryRepository();

        // LAB-03: marko je nastavnik na kursu, petar ima platformsku ulogu nastavnika, ali je na kursu student.
        _membership = new InMemoryCourseMembership();
        _membership.Set(CourseId, CallerId.From(new Caller("marko", PlatformRole.Teacher))!.Value, CourseRole.Teacher);
        _membership.Set(CourseId, CallerId.From(new Caller("petar", PlatformRole.Teacher))!.Value, CourseRole.Student);

        _service = new LaboratoryService(_repository, _membership, new FixedTimeProvider(Now), NullLogger<LaboratoryService>.Instance);
    }

    [Test]
    public async Task Create_UsesServerIdentityAsCreatorAndServerTime()
    {
        var teacher = new Caller("marko", PlatformRole.Teacher);

        LabResult result = await _service.CreateAsync(CourseId, "Kontrola pristupa", teacher, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        Laboratory? stored = await _repository.GetByIdAsync(result.Laboratory!.Id);
        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.CreatedBy, Is.EqualTo(CallerId.From(teacher)));
            Assert.That(stored.CreatedAt, Is.EqualTo(Now));
            Assert.That(stored.Status, Is.EqualTo(LabStatus.Draft));
        });
    }

    [Test]
    public async Task Create_WhenCallerIsAnonymous_StoresNothing()
    {
        LabResult result = await _service.CreateAsync(CourseId, "Kontrola pristupa", new AnonymousIdentityContext(), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Unauthenticated));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Create_WithMissingTitle_StoresNothing(string? title)
    {
        LabResult result = await _service.CreateAsync(CourseId, title, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidTitle));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WithOverlongTitle_StoresNothing()
    {
        string title = new('x', Laboratory.MaxTitleLength + 1);

        LabResult result = await _service.CreateAsync(CourseId, title, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidTitle));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    // ---------- LAB-03: vezivanje za kurs ----------

    [Test]
    public async Task Create_WhenCallerTeachesCourse_StoresCourse()
    {
        LabResult result = await _service.CreateAsync(CourseId, "Kontrola pristupa", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        Laboratory? stored = await _repository.GetByIdAsync(result.Laboratory!.Id);
        Assert.That(stored!.CourseId, Is.EqualTo(CourseId));
    }

    [TestCase(null)]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task Create_WithoutCourse_ReturnsInvalidCourseAndStoresNothing(string? courseId)
    {
        Guid? course = courseId is null ? null : Guid.Parse(courseId);

        LabResult result = await _service.CreateAsync(course, "Kontrola pristupa", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidCourse));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WhenCallerIsNotMemberOfCourse_ReturnsForbiddenAndStoresNothing()
    {
        LabResult result = await _service.CreateAsync(CourseId, "Kontrola pristupa", new Caller("jelena", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Forbidden));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WhenTeacherUsesCourseNotTaught_ReturnsForbiddenAndStoresNothing()
    {
        // Zamena ID-a kursa u zahtevu: marko je nastavnik na CourseId, ali ne i na OtherCourseId.
        LabResult result = await _service.CreateAsync(OtherCourseId, "Kontrola pristupa", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Forbidden));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WhenCallerIsStudentOnCourse_ReturnsForbiddenEvenWithTeacherPlatformRole()
    {
        // Odlucuje uloga na kursu, ne globalna uloga na platformi.
        LabResult result = await _service.CreateAsync(CourseId, "Kontrola pristupa", new Caller("petar", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Forbidden));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WhenNotTeacherAndTitleInvalid_StillReturnsForbidden()
    {
        // Redosled provera: ko nije nastavnik kursa ne dobija gresku validacije naslova.
        LabResult result = await _service.CreateAsync(CourseId, "", new Caller("jelena", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Forbidden));
    }

    [Test]
    public async Task Rename_KeepsCourse()
    {
        Laboratory lab = await CreateAsync("marko", "Stari naslov");

        await _service.RenameAsync(lab.Id, "Novi naslov", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That((await _repository.GetByIdAsync(lab.Id))!.CourseId, Is.EqualTo(CourseId));
    }

    [Test]
    public async Task Get_WhenCallerIsNotCreator_ReturnsNotFound()
    {
        Laboratory lab = await CreateAsync("marko", "Tudji nacrt");

        LabResult result = await _service.GetAsync(lab.Id, new Caller("jelena", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
        Assert.That(result.Laboratory, Is.Null);
    }

    [Test]
    public async Task Rename_KeepsIdAndCreationDataAndPersists()
    {
        Laboratory lab = await CreateAsync("marko", "Stari naslov");

        LabResult result = await _service.RenameAsync(lab.Id, "Novi naslov", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        Laboratory? stored = await _repository.GetByIdAsync(lab.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Id, Is.EqualTo(lab.Id));
            Assert.That(stored.Title, Is.EqualTo("Novi naslov"));
            Assert.That(stored.CreatedBy, Is.EqualTo(lab.CreatedBy));
            Assert.That(stored.CreatedAt, Is.EqualTo(lab.CreatedAt));
            Assert.That(stored.Status, Is.EqualTo(lab.Status));
        });
    }

    [Test]
    public async Task Rename_ByAnotherTeacher_ReturnsNotFoundAndKeepsTitle()
    {
        Laboratory lab = await CreateAsync("marko", "Originalni naslov");

        LabResult result = await _service.RenameAsync(lab.Id, "Tudja izmena", new Caller("jelena", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
        Assert.That((await _repository.GetByIdAsync(lab.Id))!.Title, Is.EqualTo("Originalni naslov"));
    }

    [Test]
    public async Task Rename_ByAnotherTeacherWithInvalidTitle_StillReturnsNotFound()
    {
        // Redosled provera: tudja laboratorija se ne otkriva ni kroz gresku validacije.
        Laboratory lab = await CreateAsync("marko", "Originalni naslov");

        LabResult result = await _service.RenameAsync(lab.Id, "", new Caller("jelena", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Rename_WithInvalidTitle_KeepsStoredTitle(string? title)
    {
        Laboratory lab = await CreateAsync("marko", "Originalni naslov");

        LabResult result = await _service.RenameAsync(lab.Id, title, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidTitle));
        Assert.That((await _repository.GetByIdAsync(lab.Id))!.Title, Is.EqualTo("Originalni naslov"));
    }

    [Test]
    public async Task Rename_WhenLaboratoryDoesNotExist_ReturnsNotFound()
    {
        LabResult result = await _service.RenameAsync(Guid.NewGuid(), "Novi naslov", new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
    }

    // ---------- LAB-03: provera konteksta pri otvaranju ----------
    // Studenti ne prolaze RequireLabAccess(Read) u FakeAccessDecision, pa se ovde testira servis direktno.

    [Test]
    public async Task Open_WhenStudentEnrolledOnCourse_ReturnsLaboratory()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");
        var student = new Caller("ana", PlatformRole.Student);
        _membership.Set(CourseId, CallerId.From(student)!.Value, CourseRole.Student);

        LabResult result = await _service.GetAsync(lab.Id, student, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        Assert.That(result.Laboratory!.Id, Is.EqualTo(lab.Id));
    }

    [Test]
    public async Task Open_WhenCourseTeacherIsCreator_ReturnsLaboratory()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");

        LabResult result = await _service.GetAsync(lab.Id, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        Assert.That(result.Laboratory!.CourseId, Is.EqualTo(CourseId));
    }

    [Test]
    public async Task Open_WhenStudentIsNotOnAnyCourse_ReturnsNotFoundEvenWithKnownId()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");

        LabResult result = await _service.GetAsync(lab.Id, new Caller("mila", PlatformRole.Student), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
        Assert.That(result.Laboratory, Is.Null);
    }

    [Test]
    public async Task Open_WhenStudentEnrolledOnOtherCourse_ReturnsNotFound()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");
        var student = new Caller("luka", PlatformRole.Student);
        _membership.Set(OtherCourseId, CallerId.From(student)!.Value, CourseRole.Student);

        LabResult result = await _service.GetAsync(lab.Id, student, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
        Assert.That(result.Laboratory, Is.Null);
    }

    [Test]
    public async Task Open_WhenCallerIsAnonymous_ReturnsUnauthenticated()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");

        LabResult result = await _service.GetAsync(lab.Id, new AnonymousIdentityContext(), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Unauthenticated));
        Assert.That(result.Laboratory, Is.Null);
    }

    [Test]
    public async Task Open_ForeignAndMissingLaboratory_GiveSameResult()
    {
        Laboratory lab = await CreateAsync("marko", "Kontrola pristupa");
        var outsider = new Caller("mila", PlatformRole.Student);

        LabResult foreign = await _service.GetAsync(lab.Id, outsider, CancellationToken.None);
        LabResult missing = await _service.GetAsync(Guid.NewGuid(), outsider, CancellationToken.None);

        Assert.That(foreign, Is.EqualTo(missing));
    }

    [Test]
    public async Task Open_WhenLaboratoryHasNoCourse_ReturnsNotFoundEvenToCreator()
    {
        // Laboratorija ucitana kroz Rehydrate bez kursa (skladiste bez kolone CourseId).
        var marko = new Caller("marko", PlatformRole.Teacher);
        Laboratory legacy = Laboratory.Rehydrate(
            Guid.NewGuid(), "Bez kursa", LabStatus.Draft, CallerId.From(marko)!.Value, Now);
        var service = new LaboratoryService(
            new LegacyRowRepository(legacy), _membership, new FixedTimeProvider(Now), NullLogger<LaboratoryService>.Instance);

        LabResult result = await service.GetAsync(legacy.Id, marko, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.NotFound));
    }

    private async Task<Laboratory> CreateAsync(string user, string title)
    {
        LabResult result = await _service.CreateAsync(CourseId, title, new Caller(user, PlatformRole.Teacher), CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Ok));
        return result.Laboratory!;
    }

    private sealed class Caller(string userId, PlatformRole role) : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public PlatformRole? Role { get; } = role;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // LAB-03: skladiste koje vraca laboratoriju bez kursa, kao PostgreSQL pre migracije Tima 20.
    private sealed class LegacyRowRepository(Laboratory stored) : ILaboratoryRepository
    {
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Laboratory?>(id == stored.Id ? stored : null);

        public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}