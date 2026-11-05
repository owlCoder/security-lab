using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Configurations;

public sealed class LaboratoryConfiguration : IEntityTypeConfiguration<LaboratoryRecord>
{
    public void Configure(EntityTypeBuilder<LaboratoryRecord> builder)
    {
        builder.ToTable("laboratories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(30);
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Goal).HasMaxLength(2000);
        builder.Property(x => x.Difficulty).HasMaxLength(30);
        builder.Property(x => x.Type).HasMaxLength(50);
        builder.Property(x => x.Tags).HasColumnType("text[]").IsRequired();
    }
}
