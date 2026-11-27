using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryRepositoryTests
{
    private static readonly Guid TeacherId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();

    private InMemoryLaboratoryRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new InMemoryLaboratoryRepository();
    }

    private async Task<Laboratory> AddLabAsync(string title = "SQL injection")
    {
        var lab = new Laboratory(CourseId, title, TeacherId);
        await _repository.AddAsync(lab);
        return lab;
    }

    [Test]
    public async Task Add_ThenGetById_ReturnsSameLaboratory()
    {
        Laboratory lab = await AddLabAsync();

        Laboratory? stored = await _repository.GetByIdAsync(lab.Id);

        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Id, Is.EqualTo(lab.Id));
        Assert.That(stored.CourseId, Is.EqualTo(CourseId));
        Assert.That(stored.Title, Is.EqualTo("SQL injection"));
        Assert.That(stored.Status, Is.EqualTo(LabStatus.Draft));
        Assert.That(stored.CreatedBy, Is.EqualTo(TeacherId));
        Assert.That(stored.CreatedAt, Is.EqualTo(lab.CreatedAt));
    }

    [Test]
    public async Task ChangeWithoutUpdate_IsNotStored()
    {
        Laboratory lab = await AddLabAsync("Original");

        lab.Rename("Nesacuvana izmena");

        Laboratory? stored = await _repository.GetByIdAsync(lab.Id);
        Assert.That(stored!.Title, Is.EqualTo("Original"));
    }

    [Test]
    public async Task GetById_WhenLaboratoryDoesNotExist_ReturnsNull()
    {
        Laboratory? stored = await _repository.GetByIdAsync(Guid.NewGuid());

        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task List_ReturnsAllStoredLaboratories()
    {
        Laboratory first = await AddLabAsync("SQL injection");
        Laboratory second = await AddLabAsync("XSS");

        IReadOnlyList<Laboratory> all = await _repository.ListAsync();

        Assert.That(all.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
    }

    [Test]
    public async Task Add_WithExistingId_IsRejectedAndKeepsOriginal()
    {
        Laboratory original = await AddLabAsync("Original");
        Laboratory sameId = Laboratory.Rehydrate(
            original.Id, CourseId, "Pokusaj prepisivanja", LabStatus.Draft, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.ThrowsAsync<DuplicateLaboratoryException>(() => _repository.AddAsync(sameId));

        Laboratory? stored = await _repository.GetByIdAsync(original.Id);
        Assert.That(stored!.Title, Is.EqualTo("Original"));
        Assert.That(stored.CreatedBy, Is.EqualTo(TeacherId));
    }

    [Test]
    public async Task AfterTwoRevisions_LaboratoryIdIsUnchanged()
    {
        Laboratory lab = await AddLabAsync();
        Guid originalId = lab.Id;

        LabRevision first = await _repository.AddRevisionAsync(originalId);
        LabRevision second = await _repository.AddRevisionAsync(originalId);

        Laboratory? stored = await _repository.GetByIdAsync(originalId);
        Assert.That(stored!.Id, Is.EqualTo(originalId));
        Assert.That(first.LaboratoryId, Is.EqualTo(originalId));
        Assert.That(second.LaboratoryId, Is.EqualTo(originalId));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task RevisionNumbers_GrowOneTwoThree()
    {
        Laboratory lab = await AddLabAsync();

        await _repository.AddRevisionAsync(lab.Id);
        await _repository.AddRevisionAsync(lab.Id);
        await _repository.AddRevisionAsync(lab.Id);

        IReadOnlyList<LabRevision> revisions = await _repository.GetRevisionsAsync(lab.Id);
        Assert.That(revisions.Select(r => r.Number), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task RevisionNumbers_AreCountedPerLaboratory()
    {
        Laboratory first = await AddLabAsync("SQL injection");
        Laboratory second = await AddLabAsync("XSS");

        await _repository.AddRevisionAsync(first.Id);
        await _repository.AddRevisionAsync(first.Id);
        LabRevision otherLab = await _repository.AddRevisionAsync(second.Id);

        Assert.That(otherLab.Number, Is.EqualTo(1));
    }

    [Test]
    public void AddRevision_ForNonExistingLaboratory_Throws()
    {
        Guid unknownId = Guid.NewGuid();

        var exception = Assert.ThrowsAsync<LaboratoryNotFoundException>(() =>
            _repository.AddRevisionAsync(unknownId));

        Assert.That(exception!.LaboratoryId, Is.EqualTo(unknownId));
    }

    [Test]
    public void GetRevisions_ForNonExistingLaboratory_Throws()
    {
        Assert.ThrowsAsync<LaboratoryNotFoundException>(() =>
            _repository.GetRevisionsAsync(Guid.NewGuid()));
    }

    [Test]
    public async Task NewLaboratory_HasNoRevisions()
    {
        Laboratory lab = await AddLabAsync();

        IReadOnlyList<LabRevision> revisions = await _repository.GetRevisionsAsync(lab.Id);

        Assert.That(revisions, Is.Empty);
    }

    [Test]
    public async Task RenameAndUpdate_KeepsIdAndRevisions()
    {
        Laboratory lab = await AddLabAsync();
        await _repository.AddRevisionAsync(lab.Id);

        lab.Rename("SQL injection - napredno");
        await _repository.UpdateAsync(lab);

        Laboratory? stored = await _repository.GetByIdAsync(lab.Id);
        IReadOnlyList<LabRevision> revisions = await _repository.GetRevisionsAsync(lab.Id);
        Assert.That(stored!.Id, Is.EqualTo(lab.Id));
        Assert.That(stored.Title, Is.EqualTo("SQL injection - napredno"));
        Assert.That(stored.CourseId, Is.EqualTo(CourseId));
        Assert.That(stored.CreatedBy, Is.EqualTo(TeacherId));
        Assert.That(stored.CreatedAt, Is.EqualTo(lab.CreatedAt));
        Assert.That(revisions, Has.Count.EqualTo(1));
    }

    [Test]
    public void Update_ForNonExistingLaboratory_Throws()
    {
        var unknown = new Laboratory(CourseId, "Nije sacuvana", TeacherId);

        Assert.ThrowsAsync<LaboratoryNotFoundException>(() => _repository.UpdateAsync(unknown));
    }

    [Test]
    public async Task ConcurrentRevisions_GetUniqueConsecutiveNumbers()
    {
        Laboratory lab = await AddLabAsync();

        LabRevision[] created = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => Task.Run(() => _repository.AddRevisionAsync(lab.Id))));

        Assert.That(created.Select(r => r.Number).OrderBy(n => n), Is.EqualTo(Enumerable.Range(1, 50)));
    }
}
