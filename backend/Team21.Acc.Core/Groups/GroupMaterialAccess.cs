using Team21.Acc.Core.Common;

namespace Team21.Acc.Core.Groups;

/// <summary>
/// Dozvola kojom jedna grupa vidi jedan ograničen materijal (ACC-06).
/// Tabela <c>groups."GroupMaterialAccess"</c>; veza više-prema-više između
/// <see cref="Group"/> i <see cref="MaterialAudience"/>.
/// <para>
/// <c>CreatedAt</c> / <c>CreatedBy</c> = kada je i ko dao dozvolu;
/// <c>IsDeleted</c> / <c>DeletedAt</c> / <c>DeletedBy</c> = kada je i ko je opozvao.
/// Opozvana dozvola ostaje kao istorija ko je šta mogao da vidi.
/// </para>
/// <para>
/// Pravila koja čuva servis: dozvola se daje samo aktivnoj grupi istog kursa kao materijal.
/// </para>
/// </summary>
public class GroupMaterialAccess : BaseEntity
{
    public Guid MaterialAudienceId { get; set; }

    public MaterialAudience MaterialAudience { get; set; } = null!;

    public Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;
}
