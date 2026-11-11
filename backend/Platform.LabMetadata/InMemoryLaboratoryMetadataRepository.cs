
using System.Collections.Concurrent;

namespace Platform.LabMetadata;

public sealed class InMemoryLaboratoryMetadataRepository
    : ILaboratoryMetadataRepository
{
    private readonly ConcurrentDictionary<Guid, LaboratoryMetadata>
        metadata = new();

    public Task<LaboratoryMetadata?> GetByLaboratoryIdAsync(
        Guid laboratoryId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        metadata.TryGetValue(laboratoryId, out var result);

        return Task.FromResult(result);
    }

    public Task SaveAsync(
        LaboratoryMetadata laboratoryMetadata,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(laboratoryMetadata);

        metadata[laboratoryMetadata.LaboratoryId] = laboratoryMetadata;

        return Task.CompletedTask;
    }
}
