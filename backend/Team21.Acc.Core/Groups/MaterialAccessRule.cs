namespace Team21.Acc.Core.Groups;

/// <summary>
/// Pravilo vidljivosti jednog materijala: kome je namenjen i koje grupe smeju da ga vide.
/// <para>
/// <see cref="AllowedGroupIds"/> sadrži samo aktivne (neopozvane) dozvole za aktivne, neobrisane
/// grupe istog kursa. Ako je <see cref="Visibility"/> = <see cref="MaterialVisibility.RestrictedToGroups"/>,
/// a lista je prazna, materijal ne vidi nijedan student (podrazumevano odbijanje).
/// </para>
/// </summary>
public sealed record MaterialAccessRule(
    MaterialRef Material,
    Guid CourseId,
    MaterialVisibility Visibility,
    IReadOnlyList<Guid> AllowedGroupIds);
