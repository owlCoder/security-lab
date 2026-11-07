using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Groups;

/// <summary>
/// Grupa studenata unutar jednog kursa (ACC-06). Tabela <c>groups."Groups"</c>.
/// <para>
/// <see cref="CourseId"/> je logička veza ka šemi <c>courses</c> (ACC-01), bez stranog ključa;
/// postojanje kursa proverava servis preko interfejsa ka ACC-01.
/// </para>
/// <para>
/// Pravila koja čuva baza: naziv je jedinstven među aktivnim, neobrisanim grupama istog kursa.
/// Pravila koja čuva servis (ACC-06 API): arhivirana grupa se ne menja i ne prima nove članove,
/// grupa ne prelazi u drugi kurs, naziv je bez razmaka na krajevima i dužine 1–<see cref="NameMaxLength"/>.
/// </para>
/// </summary>
public class Group : BaseEntity
{
    public const int NameMaxLength = 100;

    /// <summary>Kurs kojem grupa pripada. Ne menja se nakon kreiranja.</summary>
    public Guid CourseId { get; set; }

    public string Name { get; set; } = string.Empty;

    public GroupStatus Status { get; set; } = GroupStatus.Active;

    /// <summary>Kada je grupa arhivirana; <c>null</c> dok je aktivna.</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>Ko je arhivirao grupu.</summary>
    public Guid? ArchivedBy { get; set; }

    /// <summary>
    /// Token optimističke konkurentnosti. U PostgreSQL-u se mapira na sistemsku kolonu <c>xmin</c>,
    /// pa se ne pravi nova kolona; dve istovremene izmene iste grupe ne mogu obe da prođu.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>Članstva u grupi. Bivši članovi su soft-delete redovi i ne vide se u običnim upitima.</summary>
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

    /// <summary>Ograničeni materijali koje ova grupa sme da vidi.</summary>
    public ICollection<GroupMaterialAccess> MaterialAccess { get; set; } = new List<GroupMaterialAccess>();
}
