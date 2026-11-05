using System;
using System.Collections.Generic;
using System.Text;
using Platform.Prerequisites.Application;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisiteAccessServiceTests
{
    [Test]
    public async Task CheckAccessAsync_AllPrerequisitesCompleted_ReturnsUnlocked()
    {
        // Arrange
        Guid studentId = Guid.NewGuid();
        Guid laboratoryId = Guid.NewGuid();
        Guid prerequisiteId = Guid.NewGuid();

        var prerequisiteRepository = new InMemoryPrerequisiteRepository();
        var completedLaboratories = new StubCompletedLaboratories();

        await prerequisiteRepository.AddAsync(
            new LabPrerequisite(laboratoryId, prerequisiteId));

        completedLaboratories.MarkCompleted(studentId, prerequisiteId);

        var service = new PrerequisiteAccessService(
            prerequisiteRepository,
            completedLaboratories);

        // Act
        PrerequisiteAccessResult result =
            await service.CheckAccessAsync(studentId, laboratoryId);

        // Assert
        Assert.That(result.IsUnlocked, Is.True);
        Assert.That(result.MissingPrerequisites, Is.Empty);
    }

    [Test]
    public async Task CheckAccessAsync_PrerequisiteNotCompleted_ReturnsLocked()
    {
        // Arrange
        Guid studentId = Guid.NewGuid();
        Guid laboratoryId = Guid.NewGuid();
        Guid prerequisiteId = Guid.NewGuid();

        var prerequisiteRepository = new InMemoryPrerequisiteRepository();
        var completedLaboratories = new StubCompletedLaboratories();

        await prerequisiteRepository.AddAsync(
            new LabPrerequisite(laboratoryId, prerequisiteId));

        var service = new PrerequisiteAccessService(
            prerequisiteRepository,
            completedLaboratories);

        // Act
        PrerequisiteAccessResult result =
            await service.CheckAccessAsync(studentId, laboratoryId);

        // Assert
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.MissingPrerequisites, Contains.Item(prerequisiteId));
    }

    [Test]
    public async Task CheckAccessAsync_NoPrerequisites_ReturnsUnlocked()
    {
        // Arrange
        Guid studentId = Guid.NewGuid();
        Guid laboratoryId = Guid.NewGuid();

        var service = new PrerequisiteAccessService(
            new InMemoryPrerequisiteRepository(),
            new StubCompletedLaboratories());

        // Act
        PrerequisiteAccessResult result =
            await service.CheckAccessAsync(studentId, laboratoryId);

        // Assert
        Assert.That(result.IsUnlocked, Is.True);
        Assert.That(result.MissingPrerequisites, Is.Empty);
    }

    [Test]
    public async Task CheckAccessAsync_MultiplePrerequisites_ReturnsOnlyMissingOnes()
    {
        // Arrange
        Guid studentId = Guid.NewGuid();
        Guid laboratoryId = Guid.NewGuid();

        Guid firstPrerequisiteId = Guid.NewGuid();
        Guid secondPrerequisiteId = Guid.NewGuid();

        var prerequisiteRepository = new InMemoryPrerequisiteRepository();
        var completedLaboratories = new StubCompletedLaboratories();

        await prerequisiteRepository.AddAsync(
            new LabPrerequisite(laboratoryId, firstPrerequisiteId));

        await prerequisiteRepository.AddAsync(
            new LabPrerequisite(laboratoryId, secondPrerequisiteId));

        completedLaboratories.MarkCompleted(studentId, firstPrerequisiteId);

        var service = new PrerequisiteAccessService(
            prerequisiteRepository,
            completedLaboratories);

        // Act
        PrerequisiteAccessResult result =
            await service.CheckAccessAsync(studentId, laboratoryId);

        // Assert
        Assert.That(result.IsUnlocked, Is.False);
        Assert.That(result.MissingPrerequisites, Has.Count.EqualTo(1));
        Assert.That(result.MissingPrerequisites, Contains.Item(secondPrerequisiteId));
    }

    [Test]
    public void CheckAccessAsync_EmptyStudentId_ThrowsArgumentException()
    {
        var service = new PrerequisiteAccessService(
            new InMemoryPrerequisiteRepository(),
            new StubCompletedLaboratories());

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CheckAccessAsync(
                Guid.Empty,
                Guid.NewGuid()));
    }

    [Test]
    public void CheckAccessAsync_EmptyLaboratoryId_ThrowsArgumentException()
    {
        var service = new PrerequisiteAccessService(
            new InMemoryPrerequisiteRepository(),
            new StubCompletedLaboratories());

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CheckAccessAsync(
                Guid.NewGuid(),
                Guid.Empty));
    }
}
