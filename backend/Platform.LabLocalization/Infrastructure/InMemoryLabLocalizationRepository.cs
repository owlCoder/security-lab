namespace Platform.LabLocalization.Infrastructure;

using System.Collections.Concurrent;
using Platform.LabLocalization.Application;
using Platform.LabLocalization.Domain;

/// <summary>Keeps translations in memory until the module moves to PostgreSQL; they are lost on restart.</summary>
public sealed class InMemoryLabLocalizationRepository : ILabLocalizationRepository
{
    private readonly ConcurrentDictionary<(Guid RevisionId, LabLanguage Language), LabLocalization> _store = new();

    public Task<LabLocalization?> GetAsync(Guid revisionId, LabLanguage language, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault((revisionId, language)));

    public Task AddAsync(LabLocalization localization, CancellationToken ct = default)
    {
        if (!_store.TryAdd((localization.RevisionId, localization.Language), localization))
            throw new DomainRuleException(LocalizationErrorCodes.Conflict);

        return Task.CompletedTask;
    }
}
