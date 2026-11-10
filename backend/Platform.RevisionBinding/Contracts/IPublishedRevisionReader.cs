namespace Platform.RevisionBinding.Contracts;

/// <summary>
/// REV-03: Ugovor preko kojeg modul za vezivanje čita podatke o reviziji.
/// </summary>
/// <remarks>
/// Ovo je granica prema modulu revizija (REV-05, Tim 4). REV-03 zna samo za
/// ovaj interfejs; stvarna implementacija (adapter nad bazom laboratorija)
/// registruje se u Platform.Api. U testovima se koristi lažna implementacija,
/// pa se pravila provjeravaju bez baze.
///
/// Ugovor je namjerno samo za čitanje: preko njega se revizija ne može izmijeniti.
/// </remarks>
public interface IPublishedRevisionReader
{
    /// <summary>
    /// Vraća podatke o reviziji ili null ako revizija ne postoji.
    /// </summary>
    /// <param name="revisionId">Identifikator revizije iz dodjele (nikad iz klijentskog zahtjeva).</param>
    /// <param name="cancellationToken">Prekid operacije.</param>
    Task<PublishedRevisionInfo?> FindAsync(Guid revisionId, CancellationToken cancellationToken = default);
}
