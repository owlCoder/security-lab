using NUnit.Framework;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class UnavailablePrerequisiteRepositoryTests
{
    private readonly UnavailablePrerequisiteRepository _repository = new();

    [Test]
    public void AddAsync_WhenPersistenceIsUnavailable_ThrowsInvalidOperationException()
    {
        var prerequisite = new LabPrerequisite(Guid.NewGuid(), Guid.NewGuid());

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _repository.AddAsync(prerequisite));
    }

    [Test]
    public void RemoveAsync_WhenPersistenceIsUnavailable_ThrowsInvalidOperationException()
    {
        var prerequisite = new LabPrerequisite(Guid.NewGuid(), Guid.NewGuid());

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _repository.RemoveAsync(prerequisite));
    }

    [Test]
    public void GetForLaboratoryAsync_WhenPersistenceIsUnavailable_ThrowsInvalidOperationException()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _repository.GetForLaboratoryAsync(Guid.NewGuid()));
    }
}