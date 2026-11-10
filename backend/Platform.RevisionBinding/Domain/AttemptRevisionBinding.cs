namespace Platform.RevisionBinding.Domain;

/// <summary>
/// REV-03: Nepromjenljiva veza između pokušaja i tačno jedne objavljene revizije.
/// </summary>
/// <remarks>
/// Ovo se upisuje uz pokušaj u trenutku kada student počne vježbu.
/// Record nema settere, pa se veza poslije kreiranja ne može promijeniti.
///
/// ContentHash se pamti namjerno: kasnije, pri čitanju ili ocjenjivanju pokušaja,
/// ponovo se računa hash snimka i poredi sa ovim. Ako se razlikuju, sadržaj je
/// izmijenjen poslije objave i pokušaj se ne ocjenjuje nad njim (SEC-08).
/// </remarks>
/// <param name="RevisionId">Revizija za koju je pokušaj vezan.</param>
/// <param name="LaboratoryId">Laboratorija te revizije.</param>
/// <param name="RevisionNumber">Redni broj objave (1, 2, 3...), čitljiv nastavniku i studentu.</param>
/// <param name="ContentHash">SHA-256 snimka u trenutku vezivanja.</param>
public sealed record AttemptRevisionBinding(
    Guid RevisionId,
    Guid LaboratoryId,
    int RevisionNumber,
    string ContentHash);
