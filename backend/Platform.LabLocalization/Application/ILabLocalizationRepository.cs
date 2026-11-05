namespace Platform.LabLocalization.Application;

// Inside the namespace: otherwise "LabLocalization" would mean the namespace Platform.LabLocalization, not the class.
using Platform.LabLocalization.Domain;

public interface ILabLocalizationRepository
{
    Task<LabLocalization?> GetAsync(Guid revisionId, LabLanguage language, CancellationToken ct = default);

    /// <summary>Stores a new translation. A revision has at most one per language: throws CONFLICT.</summary>
    Task AddAsync(LabLocalization localization, CancellationToken ct = default);
}
