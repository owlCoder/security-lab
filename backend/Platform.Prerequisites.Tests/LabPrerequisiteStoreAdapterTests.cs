using NUnit.Framework;
using Platform.LaboratoryPersistence.Application;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class LabPrerequisiteStoreAdapterTests
{
    private readonly Guid _labA = Guid.NewGuid();
    private readonly Guid _labB = Guid.NewGuid();
    private readonly Guid _labC = Guid.NewGuid();

    private sealed class FakeStore : ILabPrerequisiteStore
    {
        private readonly List<(Guid LaboratoryId, Guid PrerequisiteId)> _items = new();

        public Task AddAsync(
            Guid laboratoryId,
            Guid prerequisiteLabId,
            CancellationToken ct = default)
        {
            _items.Add((laboratoryId, prerequisiteLabId));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            Guid laboratoryId,
            Guid prerequisiteLabId,
            CancellationToken ct = default)
        {
            _items.Remove((laboratoryId, prerequisiteLabId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetIdsAsync(
            Guid laboratoryId,
            CancellationToken ct = default)
        {
            IReadOnlyList<Guid> result = _items
                .Where(item => item.LaboratoryId == laboratoryId)
                .Select(item => item.PrerequisiteId)
                .ToList();

            return Task.FromResult(result);
        }
    }

    [Test]
    public async Task AddAsync_TwoLaboratoryCycle_IsRejected()
    {
        var adapter = new LabPrerequisiteStoreAdapter(new FakeStore());
        await adapter.AddAsync(new LabPrerequisite(_labA, _labB));

        AddPrerequisiteResult result =
            await adapter.AddAsync(new LabPrerequisite(_labB, _labA));

        Assert.That(result, Is.EqualTo(AddPrerequisiteResult.WouldCreateCycle));
    }

    [Test]
    public async Task AddAsync_ThreeLaboratoryCycle_IsRejected()
    {
        var adapter = new LabPrerequisiteStoreAdapter(new FakeStore());
        await adapter.AddAsync(new LabPrerequisite(_labA, _labB));
        await adapter.AddAsync(new LabPrerequisite(_labB, _labC));

        AddPrerequisiteResult result =
            await adapter.AddAsync(new LabPrerequisite(_labC, _labA));

        Assert.That(result, Is.EqualTo(AddPrerequisiteResult.WouldCreateCycle));
    }

    [Test]
    public async Task AddAsync_NormalChain_IsAdded()
    {
        var adapter = new LabPrerequisiteStoreAdapter(new FakeStore());
        await adapter.AddAsync(new LabPrerequisite(_labA, _labB));

        AddPrerequisiteResult result =
            await adapter.AddAsync(new LabPrerequisite(_labB, _labC));

        Assert.That(result, Is.EqualTo(AddPrerequisiteResult.Added));
    }
}