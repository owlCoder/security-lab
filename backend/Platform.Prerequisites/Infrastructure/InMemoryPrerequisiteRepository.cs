using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Infrastructure;

public sealed class InMemoryPrerequisiteRepository : IPrerequisiteRepository
{
    private readonly List<LabPrerequisite> _prerequisites = new();
    private readonly object _lock = new();

    public Task<AddPrerequisiteResult> AddAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (PrerequisiteRules.IsDuplicate(_prerequisites, prerequisite))
            {
                return Task.FromResult(AddPrerequisiteResult.Duplicate);
            }

            if (PrerequisiteRules.WouldCreateCycle(_prerequisites, prerequisite))
            {
                return Task.FromResult(AddPrerequisiteResult.WouldCreateCycle);
            }

            _prerequisites.Add(prerequisite);
            return Task.FromResult(AddPrerequisiteResult.Added);
        }
    }

    public Task<bool> RemoveAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            int removed = _prerequisites.RemoveAll(item =>
                item.LaboratoryId == prerequisite.LaboratoryId &&
                item.RequiredLaboratoryId == prerequisite.RequiredLaboratoryId);

            return Task.FromResult(removed > 0);
        }
    }

    public Task<IReadOnlyList<LabPrerequisite>> GetForLaboratoryAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<LabPrerequisite> result = _prerequisites
                .Where(item => item.LaboratoryId == laboratoryId)
                .ToList();

            return Task.FromResult(result);
        }
    }
}