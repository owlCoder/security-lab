namespace Team21.Acc.Core.Groups;

/// <summary>
/// Životni ciklus grupe (ACC-06). Grupa se arhivira, a ne briše, kako bi istorija
/// članstava i vidljivosti ostala sačuvana. Arhivirana grupa se više ne računa za vidljivost.
/// </summary>
public enum GroupStatus
{
    Active = 1,
    Archived = 2
}
