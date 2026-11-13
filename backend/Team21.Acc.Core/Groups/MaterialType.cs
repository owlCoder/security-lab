namespace Team21.Acc.Core.Groups;

/// <summary>
/// Vrsta materijala čija vidljivost može biti ograničena na grupe (ACC-06).
/// Same materijale poseduju drugi timovi (LAB / ASN); ovde se čuva samo njihov identifikator.
/// Nepoznata vrednost se odbija (podrazumevano odbijanje, ACC-05).
/// </summary>
public enum MaterialType
{
    /// <summary>Dodela laboratorije kursu (ASN-01, ASN-05).</summary>
    Assignment = 1,

    /// <summary>Prilog / prateća datoteka kursa ili laboratorije (AUT-05).</summary>
    Attachment = 2
}
