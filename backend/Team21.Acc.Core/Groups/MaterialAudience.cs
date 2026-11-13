using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Groups;

/// <summary>
/// Publika jednog materijala kursa (ACC-06, ASN-05): ceo kurs ili samo dozvoljene grupe.
/// Tabela <c>groups."MaterialAudiences"</c>.
/// <para>
/// Sam materijal poseduje drugi tim; čuvaju se samo <see cref="MaterialType"/> i <see cref="MaterialId"/>,
/// bez stranog ključa. Po materijalu postoji najviše jedna aktivna publika.
/// </para>
/// <para>
/// Ova tabela postoji da bi se razlikovalo „materijal je otvoren“ od „materijal je zatvoren,
/// a nijedna grupa još nema dozvolu“. Drugi slučaj znači da materijal ne vidi niko (ACC-05).
/// </para>
/// </summary>
public class MaterialAudience : BaseEntity
{
    /// <summary>Kurs u kojem je materijal; dozvola se daje samo grupama istog kursa.</summary>
    public Guid CourseId { get; set; }

    public MaterialType MaterialType { get; set; }

    /// <summary>Identifikator materijala u modulu koji ga poseduje.</summary>
    public Guid MaterialId { get; set; }

    public MaterialVisibility Visibility { get; set; } = MaterialVisibility.RestrictedToGroups;

    /// <summary>Token optimističke konkurentnosti (PostgreSQL <c>xmin</c>), kao kod <see cref="Group.Version"/>.</summary>
    public uint Version { get; set; }

    /// <summary>Grupe kojima je materijal dozvoljen. Opozvane dozvole su soft-delete redovi.</summary>
    public ICollection<GroupMaterialAccess> GroupAccess { get; set; } = new List<GroupMaterialAccess>();
}
