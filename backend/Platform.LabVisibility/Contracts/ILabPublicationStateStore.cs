using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Contracts;

// AUT-07 (KT2): jedina promena koju LabVisibility pravi nad javnim sadržajem je stanje
// (nacrt → objavljena → arhivirana). Upis je uslovan: stanje se menja samo ako je trenutno
// baš `expected`, pa dve istovremene promene ne mogu da se preklope (model pretnji T27).
public interface ILabPublicationStateStore
{
    // true = stanje je promenjeno; false = laboratorija ne postoji ili stanje nije bilo `expected`.
    ValueTask<bool> TryChangeStateAsync(
        Guid labId,
        LabVisibilityState expected,
        LabVisibilityState next,
        CancellationToken cancellationToken = default);
}
