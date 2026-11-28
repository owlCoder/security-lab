using System.Collections.Concurrent;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Za testove i razvoj dok ne stigne EF implementacija (Antonić).
public sealed class InMemoryLabPublicContentReader : ILabPublicContentReader, ILabPublicationStateStore
{
    private readonly ConcurrentDictionary<Guid, LabPublicContent> labs = new();

    public void Save(LabPublicContent content) => labs[content.LabId] = content;

    public ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(labs.TryGetValue(labId, out var content) ? content : null);

    // AUT-07 (KT2): uslovna promena stanja; TryUpdate uspeva samo ako se red u međuvremenu nije menjao.
    public ValueTask<bool> TryChangeStateAsync(
        Guid labId,
        LabVisibilityState expected,
        LabVisibilityState next,
        CancellationToken cancellationToken = default)
    {
        if (!labs.TryGetValue(labId, out var current) || current.State != expected)
        {
            return ValueTask.FromResult(false);
        }

        var changed = LabPublicContent.Create(
            current.LabId,
            current.CourseId,
            current.AuthorId,
            next,
            current.Title,
            current.Goal,
            current.Tasks);

        return ValueTask.FromResult(labs.TryUpdate(labId, changed, current));
    }
}
