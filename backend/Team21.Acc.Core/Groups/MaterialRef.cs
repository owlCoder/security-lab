namespace Team21.Acc.Core.Groups;

/// <summary>
/// Jednoznačna oznaka materijala drugog tima: vrsta i identifikator.
/// Odgovara paru kolona (MaterialType, MaterialId) u tabeli MaterialAudiences.
/// </summary>
public sealed record MaterialRef(MaterialType Type, Guid MaterialId);
