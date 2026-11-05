using System.Collections.Concurrent;
using Platform.Authoring.Application;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Infrastructure;

/// <summary>
/// TEMPORARY: drafts live in memory and disappear when the application stops, until the PostgreSQL
/// repository exists. Replacing it means a new IDraftRepository implementation and one changed
/// registration; the use cases and tests stay the same. It keeps and returns copies (never the
/// caller's instance), rejects duplicate ids and rejects stale saves, like a real store.
/// </summary>
public sealed class InMemoryDraftStore : IDraftRepository
{
    private readonly ConcurrentDictionary<Guid, LabDraft> _store = new();
    private readonly object _writeLock = new();

    public int Count => _store.Count;

    public Task<LabDraft?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(id, out var draft) ? Copy(draft) : null);

    public Task AddAsync(LabDraft draft, CancellationToken ct = default)
    {
        if (!_store.TryAdd(draft.Id, Copy(draft)))
            throw new DomainRuleException(AuthoringErrorCodes.Conflict);
        return Task.CompletedTask;
    }

    public Task SaveAsync(LabDraft draft, int previousVersion, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            if (!_store.TryGetValue(draft.Id, out var stored))
                throw new DomainRuleException(AuthoringErrorCodes.NotFound);
            if (stored.Version != previousVersion)
                throw new DomainRuleException(AuthoringErrorCodes.Conflict);
            _store[draft.Id] = Copy(draft);
        }
        return Task.CompletedTask;
    }

    private static LabDraft Copy(LabDraft d) =>
        LabDraft.Rehydrate(d.Id, d.OwnerId, d.SourceTemplateId, d.Status, d.Content, d.Version);
}
