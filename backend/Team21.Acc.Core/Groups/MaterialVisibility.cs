namespace Team21.Acc.Core.Groups;

/// <summary>
/// Kome je materijal kursa namenjen.
/// </summary>
public enum MaterialVisibility
{
    /// <summary>Svim aktivnim članovima kursa (uz pravila drugih modula).</summary>
    CourseWide = 1,

    /// <summary>
    /// Samo članovima grupa koje imaju aktivnu dozvolu u <see cref="GroupMaterialAccess"/>.
    /// Ograničen materijal bez ijedne aktivne dozvole ne vidi nijedan student (podrazumevano odbijanje).
    /// </summary>
    RestrictedToGroups = 2
}
