using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Groups;

/// <summary>
/// Članstvo jednog korisnika u jednoj grupi (ACC-06). Tabela <c>groups."GroupMembers"</c>.
/// <para>
/// Polja iz <see cref="BaseEntity"/> nose istoriju članstva:
/// <c>CreatedAt</c> / <c>CreatedBy</c> = kada je i ko dodao člana,
/// <c>IsDeleted</c> / <c>DeletedAt</c> / <c>DeletedBy</c> = kada je i ko uklonio člana.
/// Član se nikada ne briše fizički, pa ponovni ulazak u grupu pravi novi red, a stari ostaje
/// kao istorija (specifikacija 2.1). Uklonjena članstva se čitaju sa
/// <c>IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])</c>.
/// </para>
/// <para>
/// <see cref="UserId"/> je logička veza ka modulu identiteta (IDN), bez stranog ključa.
/// Da li je korisnik upisan na kurs (ACC-02) proverava servis pre dodavanja.
/// </para>
/// </summary>
public class GroupMember : BaseEntity
{
    public Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;

    public Guid UserId { get; set; }
}
