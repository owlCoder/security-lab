using Platform.Authoring.Domain;

namespace Platform.Authoring.Application;

public interface IDraftRepository
{
    Task<LabDraft?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Stores a new draft. An existing id is never reassigned: throws CONFLICT.</summary>
    Task AddAsync(LabDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Stores a changed draft only if the stored version is still <paramref name="previousVersion"/>;
    /// otherwise throws CONFLICT so a stale write never overwrites newer work.
    /// </summary>
    Task SaveAsync(LabDraft draft, int previousVersion, CancellationToken ct = default);
}
