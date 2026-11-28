
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

internal sealed class CourseMemberConfiguration : IEntityTypeConfiguration<CourseMember>
{
    public const string TableName = "CourseMembers";

    /// <summary>
    /// ACC-02: isti korisnik može imati najviše jedno AKTIVNO članstvo u istom kursu.
    /// Filter dozvoljava ponovni upis posle ispisa (stari zapis ostaje kao Left).
    /// </summary>
    public const string ActiveMemberIndexName = "UX_CourseMembers_Course_Account_Active";
    public const string ActiveMemberFilter = "\"Status\" = 'Active'";

    public void Configure(EntityTypeBuilder<CourseMember> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.CourseId).IsRequired();
        builder.Property(m => m.AccountId).IsRequired();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.EnrolledAt).IsRequired();
        builder.Property(m => m.EnrolledBy).IsRequired();

        builder.HasIndex(m => new { m.CourseId, m.AccountId })
            .IsUnique()
            .HasFilter(ActiveMemberFilter)
            .HasDatabaseName(ActiveMemberIndexName);

        builder.HasIndex(m => m.AccountId);
    }
}
