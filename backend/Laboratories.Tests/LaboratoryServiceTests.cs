using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

// Jedinicni testovi LAB-01 slucajeva upotrebe, bez HTTP-a.
[TestFixture]
public sealed class LaboratoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private InMemoryLaboratoryRepository _repository = null!;
    private LaboratoryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryLaboratoryRepository();
        _service = new LaboratoryService(_repository, new FixedTimeProvider(Now), NullLogger<LaboratoryService>.Instance);
    }

    [Test]
    public async Task Create_UsesServerIdentityAsCreatorAndServerTime()
    {
        var teacher = new Caller("marko", PlatformRole.Teacher);

        LabResult result = await _service.CreateAsync("Kontrola pristupa", teacher, CancellationToken.None);

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
        LabResult result = await _service.CreateAsync("Kontrola pristupa", new AnonymousIdentityContext(), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.Unauthenticated));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Create_WithMissingTitle_StoresNothing(string? title)
    {
        LabResult result = await _service.CreateAsync(title, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidTitle));
        Assert.That(await _repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Create_WithOverlongTitle_StoresNothing()
    {
        string title = new('x', Laboratory.MaxTitleLength + 1);

        LabResult result = await _service.CreateAsync(title, new Caller("marko", PlatformRole.Teacher), CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(LabResultStatus.InvalidTitle));
        Assert.That(await _repository.ListAsync(), Is.Empty);
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

    private async Task<Laboratory> CreateAsync(string user, string title)
    {
        LabResult result = await _service.CreateAsync(title, new Caller(user, PlatformRole.Teacher), CancellationToken.None);
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
}