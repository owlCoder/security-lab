namespace Platform.LabVisibility.Contracts;

// KT2 ugovor ka AUT-01/AUT-06 (nacrt) i REV (objava revizije), DRAFT dok ga ne potvrde u PR-u.
// Oni su vlasnik objave; mi pitamo koje je stanje laboratorije i od toga odlučujemo šta student vidi.
// Odgovor nikad ne nosi sadržaj ni odgovore, samo stanje.
// Koristi ga ApplyLabPublication (Cvetojević, kartica 7). Dok pravi adapter ne postoji: StubLabPublicationSource.
public enum LabPublicationStatus
{
    Draft,
    Published,
    Archived
}

public sealed record LabPublication(
    Guid LabId,
    Guid RevisionId,
    LabPublicationStatus Status,
    DateTimeOffset ChangedAt);

public interface ILabPublicationSource
{
    // null = laboratorija ne postoji kod vlasnika objave (negativan odgovor).
    ValueTask<LabPublication?> GetAsync(Guid labId, CancellationToken cancellationToken = default);
}
