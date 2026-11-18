
namespace Platform.LabMetadata;

public interface ILaboratoryMetadataRepository
{
    // Atomically creates metadata only when the laboratory has no metadata yet.
    // False means an existing record was preserved. Persistent adapters must
    // enforce this with a unique key/transaction, not a separate read and save.
    Task<bool> TryAddAsync(
        LaboratoryMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<LaboratoryMetadata?> GetByLaboratoryIdAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        LaboratoryMetadata metadata,
        CancellationToken cancellationToken = default);
}
