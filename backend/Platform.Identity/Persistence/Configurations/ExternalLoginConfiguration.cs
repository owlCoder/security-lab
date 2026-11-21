using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Identity.Entities;

namespace Platform.Identity.Persistence.Configurations;

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_logins");

        builder.HasKey(login => login.Id);
        builder.Property(login => login.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(login => login.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(login => login.Issuer)
            .HasColumnName("issuer")
            .HasMaxLength(ExternalLogin.MaxIssuerLength)
            .IsRequired();

        builder.Property(login => login.Subject)
            .HasColumnName("subject")
            .HasMaxLength(ExternalLogin.MaxSubjectLength)
            .IsRequired();

        builder.Property(login => login.LinkedAt)
            .HasColumnName("linked_at")
            .IsRequired();

        builder.Property(login => login.LastLoginAt)
            .HasColumnName("last_login_at")
            .IsRequired();

        builder.HasIndex(login => new { login.Issuer, login.Subject })
            .IsUnique()
            .HasDatabaseName("ux_external_logins_issuer_subject");

        builder.HasIndex(login => new { login.AccountId, login.Issuer })
            .IsUnique()
            .HasDatabaseName("ux_external_logins_account_issuer");

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(login => login.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}