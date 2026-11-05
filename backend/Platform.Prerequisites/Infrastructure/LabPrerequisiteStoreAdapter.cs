using Platform.LaboratoryPersistence.Application;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Infrastructure;

public sealed class LabPrerequisiteStoreAdapter : IPrerequisiteRepository
{
    private readonly ILabPrerequisiteStore _store;

    public LabPrerequisiteStoreAdapter(ILabPrerequisiteStore store)
    {
        _store = store;
    }

    public async Task<AddPrerequisiteResult> AddAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);

        IReadOnlyList<Guid> existingIds =
            await _store.GetIdsAsync(
                prerequisite.LaboratoryId,
                cancellationToken);

        if (existingIds.Contains(prerequisite.RequiredLaboratoryId))
        {
            return AddPrerequisiteResult.Duplicate;
        }

        List<LabPrerequisite> chain = await LoadChainAsync(
            prerequisite.RequiredLaboratoryId,
            cancellationToken);

        if (PrerequisiteRules.WouldCreateCycle(chain, prerequisite))
        {
            return AddPrerequisiteResult.WouldCreateCycle;
        }

        await _store.AddAsync(
            prerequisite.LaboratoryId,
            prerequisite.RequiredLaboratoryId,
            cancellationToken);

        return AddPrerequisiteResult.Added;
    }

    public async Task<bool> RemoveAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);

        IReadOnlyList<Guid> existing =
            await _store.GetIdsAsync(
                prerequisite.LaboratoryId,
                cancellationToken);

        if (!existing.Contains(prerequisite.RequiredLaboratoryId))
        {
            return false;
        }

        await _store.RemoveAsync(
            prerequisite.LaboratoryId,
            prerequisite.RequiredLaboratoryId,
            cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<LabPrerequisite>> GetForLaboratoryAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> ids =
            await _store.GetIdsAsync(laboratoryId, cancellationToken);

        return ids.Select(id =>
            new LabPrerequisite(laboratoryId, id)).ToList();
    }

    private async Task<List<LabPrerequisite>> LoadChainAsync(
        Guid startLaboratoryId,
        CancellationToken cancellationToken)
    {
        var chain = new List<LabPrerequisite>();
        var visited = new HashSet<Guid>();
        var toVisit = new Stack<Guid>();
        toVisit.Push(startLaboratoryId);

        while (toVisit.Count > 0)
        {
            Guid current = toVisit.Pop();

            if (!visited.Add(current))
            {
                continue;
            }

            IReadOnlyList<Guid> requiredIds =
                await _store.GetIdsAsync(current, cancellationToken);

            foreach (Guid requiredId in requiredIds)
            {
                chain.Add(new LabPrerequisite(current, requiredId));
                toVisit.Push(requiredId);
            }
        }

        return chain;
    }
}