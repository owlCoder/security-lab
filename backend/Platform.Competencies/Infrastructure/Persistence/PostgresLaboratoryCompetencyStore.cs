using Microsoft.EntityFrameworkCore;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;

namespace Platform.Competencies.Infrastructure.Persistence;

public sealed class PostgresLaboratoryCompetencyStore(CompetenciesDbContext context)
    : ILaboratoryCompetencyStore, ICompetencyCatalog
{
    public async Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Competencies.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async Task<bool> AddAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
    {
        _ = new LaboratoryCompetency(laboratoryId, competencyId);
        if (!await context.Competencies.AnyAsync(x => x.Id == competencyId, cancellationToken))
            throw new KeyNotFoundException("Competency does not exist in the PRG-04 catalog.");

        // The composite key and ON CONFLICT make retries and concurrent additions idempotent.
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO prg04.laboratory_competencies ("LaboratoryId", "CompetencyId")
            VALUES ({laboratoryId}, {competencyId})
            ON CONFLICT ("LaboratoryId", "CompetencyId") DO NOTHING
            """, cancellationToken) == 1;
    }

    public async Task<bool> RemoveAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
    {
        _ = new LaboratoryCompetency(laboratoryId, competencyId);
        return await context.LaboratoryCompetencies
            .Where(x => x.LaboratoryId == laboratoryId && x.CompetencyId == competencyId)
            .ExecuteDeleteAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<Competency>> GetForLaboratoryAsync(Guid laboratoryId, CancellationToken cancellationToken = default)
    {
        if (laboratoryId == Guid.Empty) throw new ArgumentException("Laboratory ID is required.", nameof(laboratoryId));
        return await (from link in context.LaboratoryCompetencies.AsNoTracking()
                      join competency in context.Competencies.AsNoTracking() on link.CompetencyId equals competency.Id
                      where link.LaboratoryId == laboratoryId
                      orderby competency.Name, competency.Id
                      select competency).ToListAsync(cancellationToken);
    }
}
