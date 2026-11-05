using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Platform.Laboratories.Infrastructure.Persistence;

#nullable disable

namespace Platform.Laboratories.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(LaboratoriesDbContext))]
    [Migration("20261101222323_AddRevisionState")]
    partial class AddRevisionState
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("laboratories")
                .HasAnnotation("ProductVersion", "10.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Platform.Laboratories.Domain.Models.CoursePublishPolicy", b =>
                {
                    b.Property<Guid>("CourseId")
                        .HasColumnType("uuid");

                    b.Property<bool>("RequiresIndependentApproval")
                        .HasColumnType("boolean");

                    b.HasKey("CourseId");

                    b.ToTable("CoursePublishPolicies", "laboratories", t =>
                        {
                            t.HasCheckConstraint("CK_CoursePublishPolicies_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                        });
                });

            modelBuilder.Entity("Platform.Laboratories.Domain.Models.LabRevision", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("AuthorId")
                        .HasColumnType("uuid");

                    b.Property<string>("ContentHash")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.Property<Guid>("CourseId")
                        .HasColumnType("uuid");

                    b.Property<long>("RowVersion")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint");

                    b.Property<int>("Status")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("CourseId");

                    b.ToTable("LabRevisions", "laboratories", t =>
                        {
                            t.HasCheckConstraint("CK_LabRevisions_AuthorId", "\"AuthorId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                            t.HasCheckConstraint("CK_LabRevisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");

                            t.HasCheckConstraint("CK_LabRevisions_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                            t.HasCheckConstraint("CK_LabRevisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                            t.HasCheckConstraint("CK_LabRevisions_RowVersion", "\"RowVersion\" > 0");

                            t.HasCheckConstraint("CK_LabRevisions_Status", "\"Status\" IN (0, 1, 2, 3)");
                        });
                });

            modelBuilder.Entity("Platform.Laboratories.Domain.Models.RevisionDecisionRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<string>("ContentHash")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.Property<DateTimeOffset>("DecidedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Decision")
                        .HasColumnType("integer");

                    b.Property<bool>("RequiresIndependentApproval")
                        .HasColumnType("boolean");

                    b.Property<Guid>("ReviewerId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("RevisionId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("RevisionId", "DecidedAt");

                    b.ToTable("RevisionDecisions", "laboratories", t =>
                        {
                            t.HasCheckConstraint("CK_RevisionDecisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");

                            t.HasCheckConstraint("CK_RevisionDecisions_Decision", "\"Decision\" IN (1, 2)");

                            t.HasCheckConstraint("CK_RevisionDecisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                            t.HasCheckConstraint("CK_RevisionDecisions_ReviewerId", "\"ReviewerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                            t.HasCheckConstraint("CK_RevisionDecisions_RevisionId", "\"RevisionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                        });
                });
#pragma warning restore 612, 618
        }
    }
}
