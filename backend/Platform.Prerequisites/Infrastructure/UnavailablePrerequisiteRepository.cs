
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Infrastructure;

public sealed class UnavailablePrerequisiteRepository : IPrerequisiteRepository
{
    private static InvalidOperationException Error() =>
        new("Prerequisite persistence is unavailable: ConnectionStrings:Authoring is not configured.");

    public Task<AddPrerequisiteResult> AddAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
        => throw Error();

    public Task<bool> RemoveAsync(
        LabPrerequisite prerequisite,
        CancellationToken cancellationToken = default)
        => throw Error();

    public Task<IReadOnlyList<LabPrerequisite>> GetForLaboratoryAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default)
        => throw Error();
}
