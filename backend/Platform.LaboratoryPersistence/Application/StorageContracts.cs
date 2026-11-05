using Platform.LabMetadata;

namespace Platform.LaboratoryPersistence.Application;

// Storage DTO, not a second LAB-01 entity.
public sealed record StoredLaboratory(Guid Id, string Title, string Status, Guid CreatedBy, DateTimeOffset CreatedAt);
public sealed record StoredRevision(Guid Id, Guid LaboratoryId, int Number);

public interface ILaboratoryStore
{
    Task AddAsync(StoredLaboratory laboratory, CancellationToken ct = default);
    Task<StoredLaboratory?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<StoredLaboratory>> ListAsync(CancellationToken ct = default);
    Task<bool> UpdateAsync(StoredLaboratory laboratory, CancellationToken ct = default);
    Task<StoredRevision?> AddRevisionAsync(Guid laboratoryId, CancellationToken ct = default);
    Task<IReadOnlyList<StoredRevision>?> GetRevisionsAsync(Guid laboratoryId, CancellationToken ct = default);
}

public interface ILaboratoryMetadataStore
{
    Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default);
    Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default);
}

public interface ILabPrerequisiteStore
{
    Task AddAsync(Guid laboratoryId, Guid prerequisiteLabId, CancellationToken ct = default);
    Task RemoveAsync(Guid laboratoryId, Guid prerequisiteLabId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetIdsAsync(Guid laboratoryId, CancellationToken ct = default);
}
