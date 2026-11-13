
using Platform.Api.Modules.Laboratories.Api;
using Platform.LaboratoryPersistence.Application;

namespace Platform.Api.Modules.Laboratories.Application;

public sealed class CatalogService(
    ILaboratoryRepository laboratoryRepository,
    ILaboratoryMetadataStore metadataStore)
{
    public async Task<int> GetLaboratoryCountAsync(
        CancellationToken cancellationToken = default)
    {
        var laboratories = await laboratoryRepository.ListAsync(
            cancellationToken);

        return laboratories.Count;
    }

    public async Task<IReadOnlyList<CatalogLaboratoryResponse>> GetCatalogAsync(
        CatalogLaboratoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var laboratories = await laboratoryRepository.ListAsync(
            cancellationToken);

        var results = new List<CatalogLaboratoryResponse>();

        foreach (var laboratory in laboratories)
        {
            var metadata = await metadataStore.GetAsync(
                laboratory.Id,
                cancellationToken);

            if (metadata is null)
            {
                continue;
            }

            results.Add(new CatalogLaboratoryResponse(
                laboratory.Id,
                laboratory.Title,
                laboratory.Status.ToString(),
                metadata.Objective,
                metadata.Difficulty.ToString(),
                metadata.Type.ToString(),
                metadata.Tags
            ));
        }

        return CatalogFilterService.Apply(results, filter);
    }
}
