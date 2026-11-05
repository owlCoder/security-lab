using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

// Skladiste laboratorija i njihovih revizija.
// KT1: InMemoryLaboratoryRepository, KT2: PostgreSQL implementacija istog ugovora.
// Provera prava se radi PRE poziva skladista (AccessCheckFilter / aplikacioni servis).
public interface ILaboratoryRepository
{
    // Cuva novu laboratoriju. Ne sme da pregazi postojecu sa istim ID-em.
    Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default);

    Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default);

    // Cuva izmenu postojece laboratorije (npr. posle Rename). ID se ne menja.
    Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default);

    // Dodaje novu reviziju; skladiste dodeljuje sledeci broj (1, 2, 3...).
    Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default);

    // Revizije laboratorije, poredjane po broju.
    Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default);
}

public sealed class LaboratoryNotFoundException(Guid laboratoryId)
    : Exception("Laboratory was not found.")
{
    public Guid LaboratoryId { get; } = laboratoryId;
}

public sealed class DuplicateLaboratoryException(Guid laboratoryId)
    : Exception("Laboratory with the same ID already exists.")
{
    public Guid LaboratoryId { get; } = laboratoryId;
}
