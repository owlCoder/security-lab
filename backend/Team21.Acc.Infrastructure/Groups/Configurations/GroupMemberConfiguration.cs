using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Team21.Acc.Core.Groups;

namespace Team21.Acc.Infrastructure.Groups.Configurations;

/// <summary>Mapiranje <see cref="GroupMember"/> na <c>groups."GroupMembers"</c>.</summary>
public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("GroupMembers");

        builder.HasKey(m => m.Id);

        // Strani ključ ka grupi je podešen u GroupConfiguration.
        builder.Property(m => m.GroupId).IsRequired();

        // Logička veza ka modulu identiteta: bez stranog ključa.
        builder.Property(m => m.UserId).IsRequired();

        // „U kojim je grupama ovaj korisnik?“ je najčešći upit provere vidljivosti.
        builder.HasIndex(m => m.UserId);

        // Najviše jedno aktivno članstvo po paru (grupa, korisnik); uklonjeni član
        // (soft delete) ne blokira ponovni ulazak u istu grupu.
        builder.HasIndex(m => new { m.GroupId, m.UserId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
    }
}
