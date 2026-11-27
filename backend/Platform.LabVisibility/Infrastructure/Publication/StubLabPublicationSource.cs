using System.Collections.Concurrent;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.LabVisibility.Infrastructure.Publication;

// Stub ugovora sa AUT-01/AUT-06 i REV dok njihov adapter ne bude spreman (KT2, stavka 5).
// Pozitivan odgovor: demo laboratorije 4444 (Published) i 5555 (Draft).
// Negativan odgovor: nepoznata laboratorija → null.
// Set() menja stanje u testovima i na demou ("objava" bez REV tima).
public sealed class StubLabPublicationSource : ILabPublicationSource
{
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<Guid, LabPublication> publications = new();

    public StubLabPublicationSource()
    {
        Set(new LabPublication(
            LabVisibilityDemoSeed.PublishedLabId,
            Guid.Parse("44444444-0000-0000-0000-000000000001"),
            LabPublicationStatus.Published,
            SeededAt));

        Set(new LabPublication(
            LabVisibilityDemoSeed.DraftLabId,
            Guid.Parse("55555555-0000-0000-0000-000000000001"),
            LabPublicationStatus.Draft,
            SeededAt));
    }

    public void Set(LabPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        publications[publication.LabId] = publication;
    }

    public ValueTask<LabPublication?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(publications.TryGetValue(labId, out var publication) ? publication : null);
}
