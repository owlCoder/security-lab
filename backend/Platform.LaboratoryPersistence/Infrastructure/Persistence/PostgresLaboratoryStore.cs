using System.Data;
using Microsoft.EntityFrameworkCore;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;
using Platform.LabMetadata;

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence;

public sealed class PostgresLaboratoryStore(LaboratoryDbContext context)
    : ILaboratoryStore, ILaboratoryMetadataStore, ILabPrerequisiteStore
{
    public async Task AddAsync(StoredLaboratory lab, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lab);
        context.Laboratories.Add(new LaboratoryRecord
        {
            Id = lab.Id, Title = lab.Title, Status = lab.Status,
            CreatedBy = lab.CreatedBy, CreatedAt = lab.CreatedAt
        });
        await context.SaveChangesAsync(ct);
    }

    public async Task<StoredLaboratory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await context.Laboratories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)) is { } r
            ? ToStored(r) : null;

    public async Task<IReadOnlyList<StoredLaboratory>> ListAsync(CancellationToken ct = default) =>
        (await context.Laboratories.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct))
        .Select(ToStored).ToList();

    public async Task<bool> UpdateAsync(StoredLaboratory lab, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lab);
        var r = await context.Laboratories.FirstOrDefaultAsync(x => x.Id == lab.Id, ct);
        if (r is null) return false;
        // Never modify immutable LAB-01 identity, author or creation time.
        r.Title = lab.Title;
        r.Status = lab.Status;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StoredRevision?> AddRevisionAsync(Guid laboratoryId, CancellationToken ct = default)
    {
        // Serializable transaction + unique (lab, number) index prevents duplicate revision numbers.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await context.Laboratories.AnyAsync(x => x.Id == laboratoryId, ct)) return null;
        var last = await context.LabRevisions.Where(x => x.LaboratoryId == laboratoryId)
            .MaxAsync(x => (int?)x.Number, ct) ?? 0;
        var r = new LabRevisionRecord { Id = Guid.NewGuid(), LaboratoryId = laboratoryId, Number = checked(last + 1) };
        context.LabRevisions.Add(r);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new StoredRevision(r.Id, r.LaboratoryId, r.Number);
    }

    public async Task<IReadOnlyList<StoredRevision>?> GetRevisionsAsync(Guid laboratoryId, CancellationToken ct = default)
    {
        if (!await context.Laboratories.AnyAsync(x => x.Id == laboratoryId, ct)) return null;
        return await context.LabRevisions.AsNoTracking().Where(x => x.LaboratoryId == laboratoryId)
            .OrderBy(x => x.Number).Select(x => new StoredRevision(x.Id, x.LaboratoryId, x.Number))
            .ToListAsync(ct);
    }

    public async Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default)
    {
        var r = await context.Laboratories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == laboratoryId, ct);
        if (r?.Goal is null || r.Difficulty is null || r.Type is null) return null;
        return new LaboratoryMetadata(r.Id, r.Goal,
            Enum.Parse<LaboratoryDifficulty>(r.Difficulty),
            Enum.Parse<LaboratoryType>(r.Type), r.Tags);
    }

    public async Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var r = await context.Laboratories.FirstOrDefaultAsync(x => x.Id == metadata.LaboratoryId, ct)
            ?? throw new KeyNotFoundException("LAB-01 laboratory must exist before LAB-02 metadata is saved.");
        r.Goal = metadata.Objective;
        r.Difficulty = metadata.Difficulty.ToString();
        r.Type = metadata.Type.ToString();
        r.Tags = metadata.Tags.ToList();
        await context.SaveChangesAsync(ct);
    }

    public async Task AddAsync(Guid laboratoryId, Guid prerequisiteLabId, CancellationToken ct = default)
    {
        if (laboratoryId == prerequisiteLabId) throw new ArgumentException("Self prerequisite is not allowed.");
        context.LabPrerequisites.Add(new LabPrerequisiteRecord
            { LaboratoryId = laboratoryId, PrerequisiteLabId = prerequisiteLabId });
        await context.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid laboratoryId, Guid prerequisiteLabId, CancellationToken ct = default)
    {
        await context.LabPrerequisites
            .Where(x => x.LaboratoryId == laboratoryId && x.PrerequisiteLabId == prerequisiteLabId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetIdsAsync(Guid laboratoryId, CancellationToken ct = default) =>
        await context.LabPrerequisites.AsNoTracking().Where(x => x.LaboratoryId == laboratoryId)
            .Select(x => x.PrerequisiteLabId).ToListAsync(ct);

    private static StoredLaboratory ToStored(LaboratoryRecord r) =>
        new(r.Id, r.Title, r.Status, r.CreatedBy, r.CreatedAt);
}
