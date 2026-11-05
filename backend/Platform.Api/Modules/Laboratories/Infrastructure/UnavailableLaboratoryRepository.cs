using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// Fail closed when no persistent store is configured outside Development.
// It does not prevent the API's unrelated, access-denied routes from starting.
public sealed class UnavailableLaboratoryRepository : ILaboratoryRepository
{
    private static InvalidOperationException Error() =>
        new("Laboratory persistence is unavailable: ConnectionStrings:Authoring is not configured.");

    public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw Error();
    public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw Error();
    public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default) => throw Error();
    public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw Error();
    public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw Error();
    public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw Error();
}
