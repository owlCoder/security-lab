using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Team21.Acc.Core.Groups;

namespace Team21.Acc.Infrastructure.Groups.Configurations;

/// <summary>Mapiranje <see cref="MaterialAudience"/> na <c>groups."MaterialAudiences"</c>.</summary>
public class MaterialAudienceConfiguration : IEntityTypeConfiguration<MaterialAudience>
{
    public void Configure(EntityTypeBuilder<MaterialAudience> builder)
    {
        builder.ToTable("MaterialAudiences");

        builder.HasKey(a => a.Id);

        // Logička veza ka šemi "courses": bez stranog ključa.
        builder.Property(a => a.CourseId).IsRequired();
        builder.HasIndex(a => a.CourseId);

        builder.Property(a => a.MaterialType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        // Materijal poseduje drugi tim: bez stranog ključa.
        builder.Property(a => a.MaterialId).IsRequired();

        builder.Property(a => a.Visibility)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.Version).IsRowVersion();

        // Jedna aktivna publika po materijalu.
        builder.HasIndex(a => new { a.MaterialType, a.MaterialId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasMany(a => a.GroupAccess)
            .WithOne(g => g.MaterialAudience)
            .HasForeignKey(g => g.MaterialAudienceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
