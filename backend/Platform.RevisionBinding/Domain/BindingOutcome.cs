namespace Platform.RevisionBinding.Domain;

/// <summary>
/// REV-03: Stabilni kodovi odluke o vezivanju pokušaja za reviziju.
/// </summary>
/// <remarks>
/// Svaki ishod ima svoj kod, a ne samo true/false, kako bi test, audit zapis
/// i pregledač mogli tačno da vide ZAŠTO je vezivanje odbijeno.
/// Kodovi su interni: klijentu se ne vraćaju detalji koji bi otkrili
/// postojanje ili stanje tuđe revizije.
/// </remarks>
public enum BindingOutcome
{
    /// <summary>Pokušaj je vezan za objavljenu reviziju.</summary>
    Bound = 0,

    /// <summary>Ulaz nije ispravan (npr. prazan identifikator).</summary>
    InvalidInput = 1,

    /// <summary>Revizija sa datim identifikatorom ne postoji.</summary>
    RevisionNotFound = 2,

    /// <summary>Revizija nije u stanju Published (nacrt, na pregledu ili arhivirana).</summary>
    NotPublished = 3,

    /// <summary>Revizija pripada drugom kursu od dodjele.</summary>
    CourseMismatch = 4
}
