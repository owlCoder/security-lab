using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// Catalog reads the same metadata repository that the teacher API writes.
// This bridge owns no data and creates no second in-memory store.
public sealed class MetadataCatalogStore(ILaboratoryMetadataRepository repository)
    : ILaboratoryMetadataStore
{
    public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default) =>
        repository.GetByLaboratoryIdAsync(laboratoryId, ct);

    public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) =>
        repository.SaveAsync(metadata, ct);
}
