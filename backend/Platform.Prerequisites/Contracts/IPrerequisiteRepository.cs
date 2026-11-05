using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Contracts;

public interface IPrerequisiteRepository
{
    Task<AddPrerequisiteResult> AddAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LabPrerequisite>> GetForLaboratoryAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default);
}