using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Team21.Acc.Core.Groups;

namespace Team21.Acc.Infrastructure.Groups.Configurations;

/// <summary>Mapiranje <see cref="GroupMaterialAccess"/> na <c>groups."GroupMaterialAccess"</c>.</summary>
public class GroupMaterialAccessConfiguration : IEntityTypeConfiguration<GroupMaterialAccess>
{
    public void Configure(EntityTypeBuilder<GroupMaterialAccess> builder)
    {
        builder.ToTable("GroupMaterialAccess");

        builder.HasKey(a => a.Id);

        // Oba strana ključa (ka grupi i ka publici materijala) podešena su na strani roditelja,
        // u GroupConfiguration i MaterialAudienceConfiguration. Obe tabele su u istoj šemi.
        builder.Property(a => a.MaterialAudienceId).IsRequired();
        builder.Property(a => a.GroupId).IsRequired();

        builder.HasIndex(a => a.GroupId);

        // Najviše jedna aktivna dozvola po paru (materijal, grupa); opozvana dozvola
        // (soft delete) ne blokira ponovno davanje.
        builder.HasIndex(a => new { a.MaterialAudienceId, a.GroupId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
    }
}
