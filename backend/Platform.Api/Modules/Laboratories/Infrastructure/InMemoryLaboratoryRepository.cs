using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// KT1: skladiste u memoriji. Podaci nestaju posle restarta.
// KT2: zamena PostgreSQL implementacijom istog ILaboratoryRepository ugovora.
//
// Cuva se kopija podataka (kao red u bazi), a ne sam objekat:
// izmena vazi tek posle UpdateAsync, a ucitavanje ide kroz Laboratory.Rehydrate.
public sealed class InMemoryLaboratoryRepository : ILaboratoryRepository
{
    private sealed record StoredLaboratory(
        Guid Id, string Title, LabStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt);

    private readonly object _sync = new();
    private readonly Dictionary<Guid, StoredLaboratory> _laboratories = new();
    private readonly Dictionary<Guid, List<LabRevision>> _revisions = new();

    public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(laboratory);

        lock (_sync)
        {
            // Postojeca laboratorija se nikad ne pregazuje: ID je trajan identitet.
            if (!_laboratories.TryAdd(laboratory.Id, ToStored(laboratory)))
            {
                throw new DuplicateLaboratoryException(laboratory.Id);
            }

            _revisions[laboratory.Id] = new List<LabRevision>();
        }

        return Task.CompletedTask;
    }

    public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Laboratory? laboratory = _laboratories.TryGetValue(id, out StoredLaboratory? stored)
                ? ToDomain(stored)
                : null;

            return Task.FromResult(laboratory);
        }
    }

    public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<Laboratory> result = _laboratories.Values
                .OrderBy(l => l.CreatedAt)
                .Select(ToDomain)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(laboratory);

        lock (_sync)
        {
            if (!_laboratories.TryGetValue(laboratory.Id, out StoredLaboratory? existing))
            {
                throw new LaboratoryNotFoundException(laboratory.Id);
            }

            // Autor i vreme kreiranja se ne menjaju izmenom; menjaju se naslov i status.
            _laboratories[laboratory.Id] = existing with
            {
                Title = laboratory.Title,
                Status = laboratory.Status,
            };
        }

        return Task.CompletedTask;
    }

    public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            // Revizija nepostojece laboratorije se odbija i nista se ne upisuje.
            if (!_revisions.TryGetValue(laboratoryId, out List<LabRevision>? revisions))
            {
                throw new LaboratoryNotFoundException(laboratoryId);
            }

            // Broj se dodeljuje pod zakljucavanjem, pa dve istovremene revizije
            // ne mogu dobiti isti broj.
            var revision = new LabRevision(laboratoryId, revisions.Count + 1);
            revisions.Add(revision);
            return Task.FromResult(revision);
        }
    }

    public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_revisions.TryGetValue(laboratoryId, out List<LabRevision>? revisions))
            {
                throw new LaboratoryNotFoundException(laboratoryId);
            }

            IReadOnlyList<LabRevision> result = revisions.OrderBy(r => r.Number).ToList();
            return Task.FromResult(result);
        }
    }

    private static StoredLaboratory ToStored(Laboratory laboratory) =>
        new(laboratory.Id, laboratory.Title, laboratory.Status, laboratory.CreatedBy, laboratory.CreatedAt);

    private static Laboratory ToDomain(StoredLaboratory stored) =>
        Laboratory.Rehydrate(stored.Id, stored.Title, stored.Status, stored.CreatedBy, stored.CreatedAt);
}
