
namespace Platform.LabMetadata;

public interface ILaboratoryMetadataRepository
{
    Task<LaboratoryMetadata?> GetByLaboratoryIdAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        LaboratoryMetadata metadata,
        CancellationToken cancellationToken = default);
}
