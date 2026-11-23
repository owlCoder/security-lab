using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Exceptions;

namespace Platform.Prerequisites.Infrastructure;

// Fail closed when prerequisite persistence is not configured.
// It allows unrelated API routes to start, but prerequisite operations remain unavailable.
public sealed class UnavailablePrerequisiteRepository : IPrerequisiteRepository
{
    private static PrerequisitePersistenceUnavailableException Error() =>
        new();

    public Task<AddPrerequisiteResult> AddAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default) =>
        throw Error();

    public Task<bool> RemoveAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default) =>
        throw Error();

    public Task<IReadOnlyList<LabPrerequisite>> GetForLaboratoryAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default) =>
        throw Error();
}