using Platform.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Identity.Persistence;

internal sealed class BackupCodeStorageEntryConfiguration
    : IEntityTypeConfiguration<BackupCodeStorageEntry>
{
    public void Configure(
        EntityTypeBuilder<BackupCodeStorageEntry> builder)
    {
        builder.ToTable(
            "backup_codes",
            PlatformIdentityDbContext.SchemaName);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.IsUsed)
            .HasColumnName("is_used")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.UsedAt)
            .HasColumnName("used_at")
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.UserId,
            x.CodeHash
        })
        .IsUnique();

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
