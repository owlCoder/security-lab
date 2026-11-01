using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Identity.Domain;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
   
        public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(session => session.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(session => session.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(32)          // SHA-256 = 32 bajta
            .IsRequired();

        builder.Property(session => session.SessionHandle)
            .HasColumnName("session_handle")
            .IsRequired();

        builder.Property(session => session.AuthLevel)
            .HasColumnName("auth_level")
            .HasConversion<string>()   // isto kao Status kod naloga
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(session => session.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(session => session.LastActivityAt)
            .HasColumnName("last_activity_at")
            .IsRequired();

        builder.Property(session => session.RevokedAt)
            .HasColumnName("revoked_at")
            .IsConcurrencyToken();     // samo jedan zahtev moze da opozove sesiju

        builder.HasIndex(session => session.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_sessions_token_hash");

        builder.HasIndex(session => session.SessionHandle)
            .IsUnique()
            .HasDatabaseName("ux_sessions_session_handle");

        builder.HasIndex(session => session.UserId)
            .HasDatabaseName("ix_sessions_user_id");

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

}
