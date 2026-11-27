using Microsoft.EntityFrameworkCore;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// LAB-02 adapter over the existing catalog storage; owns no schema or migrations.
public sealed class PersistentMetadataRepository(
    LaboratoryDbContext context,
    ILaboratoryMetadataStore store) : ILaboratoryMetadataRepository
{
    public Task<LaboratoryMetadata?> GetByLaboratoryIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.GetAsync(id, cancellationToken);

    public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken cancellationToken = default) =>
        store.SaveAsync(metadata, cancellationToken);

    public async Task<bool> TryAddAsync(LaboratoryMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var difficulty = metadata.Difficulty.ToString();
        var type = metadata.Type.ToString();
        var tags = metadata.Tags.ToList();
        // One conditional UPDATE is atomic across requests and app instances.
        // Metadata is absent under the same rule used by PostgresLaboratoryStore.GetAsync.
        var affected = await context.Laboratories
            .Where(row => row.Id == metadata.LaboratoryId &&
                (row.Goal == null || row.Difficulty == null || row.Type == null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Goal, metadata.Objective)
                .SetProperty(row => row.Difficulty, difficulty)
                .SetProperty(row => row.Type, type)
                .SetProperty(row => row.Tags, tags), cancellationToken);
        return affected == 1;
    }
}
