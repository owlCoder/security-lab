using Platform.Authoring.Application;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Tests.Fakes;

/// <summary>
/// Test double with the same contract as a real store: it keeps and returns copies
/// (never the caller's instance), rejects duplicate ids and rejects stale saves.
/// </summary>
public sealed class InMemoryDraftRepository : IDraftRepository
{
    private readonly Dictionary<Guid, LabDraft> _store = new();

    public int Count => _store.Count;

    public LabDraft? Peek(Guid id) => _store.TryGetValue(id, out var d) ? Copy(d) : null;

    public Task<LabDraft?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Peek(id));

    public Task AddAsync(LabDraft draft, CancellationToken ct = default)
    {
        if (_store.ContainsKey(draft.Id)) throw new DomainRuleException(AuthoringErrorCodes.Conflict);
        _store[draft.Id] = Copy(draft);
        return Task.CompletedTask;
    }

    public Task SaveAsync(LabDraft draft, int previousVersion, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(draft.Id, out var stored)) throw new DomainRuleException(AuthoringErrorCodes.NotFound);
        if (stored.Version != previousVersion) throw new DomainRuleException(AuthoringErrorCodes.Conflict);
        _store[draft.Id] = Copy(draft);
        return Task.CompletedTask;
    }

    private static LabDraft Copy(LabDraft d) =>
        LabDraft.Rehydrate(d.Id, d.OwnerId, d.SourceTemplateId, d.Status, d.Content, d.Version);
}
