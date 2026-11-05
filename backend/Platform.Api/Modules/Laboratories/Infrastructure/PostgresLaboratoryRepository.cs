using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LaboratoryPersistence.Application;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// LAB-01 contract adapter: map to the existing domain, never invent a second domain model.
public sealed class PostgresLaboratoryRepository(ILaboratoryStore store) : ILaboratoryRepository
{
    public async Task AddAsync(Laboratory laboratory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(laboratory);
        try
        {
            await store.AddAsync(new StoredLaboratory(laboratory.Id, laboratory.Title,
                laboratory.Status.ToString(), laboratory.CreatedBy, laboratory.CreatedAt), ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DuplicateLaboratoryException(laboratory.Id);
        }
    }

    public async Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await store.GetByIdAsync(id, ct)) is { } row ? ToDomain(row) : null;

    public async Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken ct = default) =>
        (await store.ListAsync(ct)).Select(ToDomain).ToList();

    public async Task UpdateAsync(Laboratory laboratory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(laboratory);
        if (!await store.UpdateAsync(new StoredLaboratory(laboratory.Id, laboratory.Title,
            laboratory.Status.ToString(), laboratory.CreatedBy, laboratory.CreatedAt), ct))
            throw new LaboratoryNotFoundException(laboratory.Id);
    }

    public async Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken ct = default)
    {
        var revision = await store.AddRevisionAsync(laboratoryId, ct)
            ?? throw new LaboratoryNotFoundException(laboratoryId);
        return new LabRevision(revision.Id, revision.LaboratoryId, revision.Number);
    }

    public async Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken ct = default) =>
        (await store.GetRevisionsAsync(laboratoryId, ct)
            ?? throw new LaboratoryNotFoundException(laboratoryId))
        .Select(x => new LabRevision(x.Id, x.LaboratoryId, x.Number)).ToList();

    private static Laboratory ToDomain(StoredLaboratory row) =>
        Laboratory.Rehydrate(row.Id, row.Title, Enum.Parse<LabStatus>(row.Status), row.CreatedBy, row.CreatedAt);
}
