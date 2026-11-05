using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Infrastructure.Persistence.Configurations;

public sealed class RevisionDecisionRecordConfiguration : IEntityTypeConfiguration<RevisionDecisionRecord>
{
    public void Configure(EntityTypeBuilder<RevisionDecisionRecord> builder)
    {
        builder.ToTable("RevisionDecisions", table =>
        {
            table.HasCheckConstraint("CK_RevisionDecisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_RevisionDecisions_RevisionId", "\"RevisionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_RevisionDecisions_ReviewerId", "\"ReviewerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_RevisionDecisions_Decision", "\"Decision\" IN (1, 2)");
            table.HasCheckConstraint("CK_RevisionDecisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id).ValueGeneratedNever();
        builder.Property(decision => decision.RevisionId).IsRequired();
        builder.Property(decision => decision.ReviewerId).IsRequired();
        builder.Property(decision => decision.Decision).HasConversion<int>().IsRequired();
        builder.Property(decision => decision.DecidedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(decision => decision.ContentHash).HasMaxLength(RevisionContentHash.HexLength).IsRequired();
        builder.Property(decision => decision.RequiresIndependentApproval).IsRequired();
        builder.HasIndex(decision => new { decision.RevisionId, decision.DecidedAt });
    }
}
