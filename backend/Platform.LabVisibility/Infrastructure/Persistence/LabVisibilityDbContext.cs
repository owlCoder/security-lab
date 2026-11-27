using Microsoft.EntityFrameworkCore;

namespace Platform.LabVisibility.Infrastructure.Persistence;

public sealed class LabVisibilityDbContext(DbContextOptions<LabVisibilityDbContext> options)
    : DbContext(options)
{
    public const string Schema = "lab_visibility";

    public DbSet<LabPublicContentRow> PublicContents => Set<LabPublicContentRow>();
    public DbSet<LabPublicTaskRow> PublicTasks => Set<LabPublicTaskRow>();
    public DbSet<EvaluatorKeyRow> EvaluatorKeys => Set<EvaluatorKeyRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        model.Entity<LabPublicContentRow>(e =>
        {
            e.ToTable("lab_public_content");
            e.HasKey(x => x.LabId);
            e.Property(x => x.LabId).HasColumnName("lab_id");
            e.Property(x => x.CourseId).HasColumnName("course_id");
            e.Property(x => x.AuthorId).HasColumnName("author_id");
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(16).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Goal).HasColumnName("goal").IsRequired();

            e.HasMany(x => x.Tasks).WithOne()
                .HasForeignKey(t => t.LabId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<LabPublicTaskRow>(e =>
        {
            e.ToTable("lab_public_task");
            e.HasKey(x => new { x.LabId, x.TaskId });
            e.Property(x => x.LabId).HasColumnName("lab_id");
            e.Property(x => x.TaskId).HasColumnName("task_id");
            e.Property(x => x.Order).HasColumnName("task_order"); // "order" je rezervisana reč u SQL-u
            e.Property(x => x.Prompt).HasColumnName("prompt").IsRequired();
            e.Property(x => x.Options).HasColumnName("options").IsRequired();
        });

        // Posebna tabela. Pokazuje na javni zadatak, a javni zadatak ne zna za nju
        // (nema navigacije), pa Include nad zadacima ne može da povuče odgovore.
        model.Entity<EvaluatorKeyRow>(e =>
        {
            e.ToTable("lab_evaluator_key");
            e.HasKey(x => new { x.LabId, x.TaskId });
            e.Property(x => x.LabId).HasColumnName("lab_id");
            e.Property(x => x.TaskId).HasColumnName("task_id");
            e.Property(x => x.ExpectedAnswer).HasColumnName("expected_answer").IsRequired();

            e.HasOne<LabPublicTaskRow>().WithMany()
                .HasForeignKey(x => new { x.LabId, x.TaskId })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
