using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Team21.Acc.Core.Groups;

namespace Team21.Acc.Infrastructure.Groups.Configurations;

/// <summary>
/// Mapiranje <see cref="Group"/> na <c>groups."Groups"</c>. GroupsDbContext ga pronalazi sam,
/// jer je u imenskom prostoru <c>Team21.Acc.Infrastructure.Groups</c>.
/// </summary>
public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups");

        builder.HasKey(g => g.Id);

        // Logička veza ka šemi "courses": samo kolona i indeks, bez stranog ključa.
        builder.Property(g => g.CourseId).IsRequired();
        builder.HasIndex(g => g.CourseId);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(Group.NameMaxLength);

        // Enum se čuva kao tekst, da bi baza bila čitljiva i da dodavanje vrednosti ne pomeri brojeve.
        builder.Property(g => g.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // uint + IsRowVersion Npgsql mapira na sistemsku kolonu xmin.
        builder.Property(g => g.Version).IsRowVersion();

        // Naziv je jedinstven među aktivnim, neobrisanim grupama kursa;
        // naziv arhivirane ili obrisane grupe može ponovo da se iskoristi.
        builder.HasIndex(g => new { g.CourseId, g.Name })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active' AND \"IsDeleted\" = false");

        builder.HasMany(g => g.Members)
            .WithOne(m => m.Group)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.MaterialAccess)
            .WithOne(a => a.Group)
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
