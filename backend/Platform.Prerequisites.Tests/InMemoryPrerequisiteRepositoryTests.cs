using NUnit.Framework;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class InMemoryPrerequisiteRepositoryTests
{
    private readonly Guid _labA = Guid.NewGuid();
    private readonly Guid _labB = Guid.NewGuid();

    [Test]
    public async Task AddAsync_NewPrerequisite_IsSaved()
    {
        var repository = new InMemoryPrerequisiteRepository();

        AddPrerequisiteResult result =
            await repository.AddAsync(new LabPrerequisite(_labA, _labB));
        var saved = await repository.GetForLaboratoryAsync(_labA);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(AddPrerequisiteResult.Added));
            Assert.That(saved, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AddAsync_Duplicate_IsRejected()
    {
        var repository = new InMemoryPrerequisiteRepository();
        await repository.AddAsync(new LabPrerequisite(_labA, _labB));

        AddPrerequisiteResult result =
            await repository.AddAsync(new LabPrerequisite(_labA, _labB));
        var saved = await repository.GetForLaboratoryAsync(_labA);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(AddPrerequisiteResult.Duplicate));
            Assert.That(saved, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AddAsync_Cycle_IsRejected()
    {
        var repository = new InMemoryPrerequisiteRepository();
        await repository.AddAsync(new LabPrerequisite(_labA, _labB));

        AddPrerequisiteResult result =
            await repository.AddAsync(new LabPrerequisite(_labB, _labA));
        var saved = await repository.GetForLaboratoryAsync(_labB);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(AddPrerequisiteResult.WouldCreateCycle));
            Assert.That(saved, Is.Empty);
        });
    }

    [Test]
    public async Task RemoveAsync_ExistingPrerequisite_ReturnsTrue()
    {
        var repository = new InMemoryPrerequisiteRepository();
        await repository.AddAsync(new LabPrerequisite(_labA, _labB));

        bool removed = await repository.RemoveAsync(new LabPrerequisite(_labA, _labB));
        var saved = await repository.GetForLaboratoryAsync(_labA);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(saved, Is.Empty);
        });
    }

    [Test]
    public async Task RemoveAsync_MissingPrerequisite_ReturnsFalse()
    {
        var repository = new InMemoryPrerequisiteRepository();

        bool removed = await repository.RemoveAsync(new LabPrerequisite(_labA, _labB));

        Assert.That(removed, Is.False);
    }
}