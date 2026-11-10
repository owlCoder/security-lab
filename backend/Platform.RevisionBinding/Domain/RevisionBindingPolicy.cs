using Platform.RevisionBinding.Contracts;

namespace Platform.RevisionBinding.Domain;

/// <summary>
/// REV-03: Pravilo koje odlučuje da li se novi pokušaj smije vezati za datu reviziju.
/// </summary>
/// <remarks>
/// Pravilo je "čista" funkcija: ne čita bazu, sat ni mrežu, već samo podatke
/// koje dobije. Isti ulaz uvijek daje istu odluku, pa se lako i pouzdano testira.
///
/// Provjere idu redom, a prva koja ne prođe određuje razlog odbijanja.
/// Podrazumijevano je odbijanje: pokušaj se vezuje SAMO ako su sve provjere prošle.
///
/// Trenutno implementirano (KT1):
///   1. revizija postoji
///   2. revizija je objavljena
/// Planirano (KT2): provjera kursa dodjele i ispravnosti broja/hasha.
/// </remarks>
public static class RevisionBindingPolicy
{
    /// <summary>
    /// Donosi odluku o vezivanju.
    /// </summary>
    /// <param name="revision">Podaci o reviziji ili null ako revizija nije pronađena.</param>
    /// <returns>Bound sa nepromjenljivom vezom ili Rejected sa razlogom.</returns>
    public static BindingResult Decide(PublishedRevisionInfo? revision)
    {
        // 1. Revizija mora postojati.
        if (revision is null)
            return new BindingResult.Rejected(BindingOutcome.RevisionNotFound);

        // 2. Samo objavljena revizija je zaključan snimak za koji se smije vezati.
        //    Nacrt i revizija na pregledu se još mogu mijenjati, a arhivirana
        //    više ne prima nove pokušaje (REV-04).
        if (revision.State != RevisionState.Published)
            return new BindingResult.Rejected(BindingOutcome.NotPublished);

        // Objavljena revizija mora imati redni broj. Ako ga nema, podaci su
        // nekonzistentni i ne vezujemo se za nešto što ne možemo jasno označiti.
        if (revision.Number is not int number)
            return new BindingResult.Rejected(BindingOutcome.InvalidInput);

        // Sve provjere su prošle: pravimo vezu koja pamti tačnu reviziju i njen hash.
        return new BindingResult.Bound(new AttemptRevisionBinding(
            revision.RevisionId,
            revision.LaboratoryId,
            number,
            revision.ContentHash));
    }
}
