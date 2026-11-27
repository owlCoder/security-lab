
using NUnit.Framework;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class UnavailablePrerequisiteRepositoryTests
{
    [Test]
    public void Get_WhenStorageUnavailable_Throws()
    {
        var repository = new UnavailablePrerequisiteRepository();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.GetForLaboratoryAsync(
                Guid.NewGuid()));
    }

    [Test]
    public void Add_WhenStorageUnavailable_Throws()
    {
        var repository = new UnavailablePrerequisiteRepository();

        var prerequisite = new LabPrerequisite(
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.AddAsync(prerequisite));
    }

    [Test]
    public void Remove_WhenStorageUnavailable_Throws()
    {
        var repository = new UnavailablePrerequisiteRepository();

        var prerequisite = new LabPrerequisite(
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await repository.RemoveAsync(prerequisite));
    }
}
