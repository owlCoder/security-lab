using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Identity.Entities;

namespace Platform.Identity.Persistence.Configurations;

public class MfaCredentialsConfiguration : IEntityTypeConfiguration<MfaCredentials>
{
    public void Configure(EntityTypeBuilder<MfaCredentials> builder)
    {
        builder.ToTable("mfa_credentials");

        builder.HasKey(credentials => credentials.UserId);
        builder.Property(credentials => credentials.UserId)
            .HasColumnName("user_id")
            .ValueGeneratedNever();

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(credentials => credentials.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(credentials => credentials.UserId)
            .HasDatabaseName("ix_mfa_credentials_user_id");

        builder.Property(credentials => credentials.MfaEnabled)
            .HasColumnName("mfa_enabled")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(credentials => credentials.TotpSecretCiphertext)
            .HasColumnName("totp_secret_ciphertext")
            .HasMaxLength(512); // Cyphered base32 string

        builder.Property(credentials => credentials.TotpSecretKeyVersion)
            .HasColumnName("totp_secret_key_version");

        builder.Property(credentials => credentials.PendingTotpSecretCiphertext)
            .HasColumnName("pending_totp_secret_ciphertext")
            .HasMaxLength(512); // Cyphered base32 string+

        builder.Property(credentials => credentials.PendingTotpSecretKeyVersion)
            .HasColumnName("pending_totp_secret_key_version");

        builder.Property(credentials => credentials.PendingEnrollmentExpiresAt)
            .HasColumnName("pending_enrollment_expires_at");

        builder.Property(credentials => credentials.LastTotpTimeStep)
            .HasColumnName("last_totp_time_step");

        builder.Property(credentials => credentials.MfaFailedAttempts)
            .HasColumnName("mfa_failed_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(credentials => credentials.MfaLockedUntil)
            .HasColumnName("mfa_locked_until");
    }
}