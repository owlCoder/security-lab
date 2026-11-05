using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Infrastructure.Persistence.Configurations;

public sealed class CoursePublishPolicyConfiguration : IEntityTypeConfiguration<CoursePublishPolicy>
{
    public void Configure(EntityTypeBuilder<CoursePublishPolicy> builder)
    {
        builder.ToTable("CoursePublishPolicies", table => table.HasCheckConstraint(
            "CK_CoursePublishPolicies_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(policy => policy.CourseId);
        builder.Property(policy => policy.CourseId).ValueGeneratedNever();
        builder.Property(policy => policy.RequiresIndependentApproval).IsRequired();
    }
}
