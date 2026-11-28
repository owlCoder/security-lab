
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

/// <summary>ACC-07-2: tabela membership."CourseDelegations". Zapis se nikad ne briše.</summary>
internal sealed class CourseDelegationConfiguration : IEntityTypeConfiguration<CourseDelegation>
{
    public const string TableName = "CourseDelegations";
    public const string PendingExpiryIndexName = "IX_CourseDelegations_PendingExpiry";
    public const string PendingExpiryFilter = "\"RevokedAt\" IS NULL AND \"ExpiryRecordedAt\" IS NULL";

    public void Configure(EntityTypeBuilder<CourseDelegation> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.CourseId).IsRequired();
        builder.Property(d => d.GranteeId).IsRequired();
        builder.Property(d => d.Action).HasMaxLength(CourseDelegation.MaxActionLength).IsRequired();
        builder.Property(d => d.GrantedBy).IsRequired();
        builder.Property(d => d.GrantedAt).IsRequired();
        builder.Property(d => d.ExpiresAt).IsRequired();

        // Provera pristupa pita: koje delegacije ima korisnik X u kursu K.
        builder.HasIndex(d => new { d.CourseId, d.GranteeId });

        // Pozadinski posao (ACC-07-8) traži samo istekle čiji istek još nije upisan.
        builder.HasIndex(d => d.ExpiresAt)
            .HasFilter(PendingExpiryFilter)
            .HasDatabaseName(PendingExpiryIndexName);
    }
}
