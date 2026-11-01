using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Identity.Domain;

namespace Platform.Identity.Data;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(account => account.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(account => account.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(100);

        builder.HasIndex(account => account.Email)
            .IsUnique()
            .HasDatabaseName("ux_accounts_email");
    }
}
