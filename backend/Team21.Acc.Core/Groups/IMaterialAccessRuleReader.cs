namespace Team21.Acc.Core.Groups;

/// <summary>
/// Čitanje pravila vidljivosti materijala (ACC-06, ASN-05). Samo čitanje, bez provere prava:
/// ko sme da vidi pravilo i šta iz njega sledi odlučuje serverska provera vidljivosti.
/// Ne izlagati direktno kroz endpoint.
/// </summary>
public interface IMaterialAccessRuleReader
{
    /// <summary>
    /// Pravilo za jedan materijal, ili <c>null</c> ako za njega ne postoji zapis
    /// (ili je oznaka neispravna: prazan ID, nepoznata vrsta).
    /// </summary>
    Task<MaterialAccessRule?> FindAsync(MaterialRef material, CancellationToken ct = default);

    /// <summary>Sva pravila materijala u kursu. Prazna lista ako ih nema.</summary>
    Task<IReadOnlyList<MaterialAccessRule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default);
}
