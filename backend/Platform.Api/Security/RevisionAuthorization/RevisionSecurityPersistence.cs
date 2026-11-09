using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Api.Security.RevisionAuthorization;

public interface IRevisionCoursePermissionReader
{
    ValueTask<bool> IsGrantedAsync(Guid userId, Guid courseId, string action, CancellationToken cancellationToken = default);
}

/// <summary>Explicit, server-managed course grants. No public endpoint can create these rows.</summary>
public sealed class RevisionCoursePermission
{
    public Guid UserId { get; init; }
    public Guid CourseId { get; init; }
    public required string Action { get; init; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class RevisionSecurityDbContext(DbContextOptions<RevisionSecurityDbContext> options) : DbContext(options)
{
    public DbSet<RevisionCoursePermission> Permissions => Set<RevisionCoursePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => Configure(modelBuilder);

    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("revision_security");
        modelBuilder.HasAnnotation("ProductVersion", "10.0.14").HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.UseIdentityByDefaultColumns();
        var grant = modelBuilder.Entity<RevisionCoursePermission>();
        grant.Property(value => value.UserId).HasColumnType("uuid");
        grant.Property(value => value.CourseId).HasColumnType("uuid");
        grant.Property(value => value.IsActive).HasColumnType("boolean");
        grant.Property(value => value.ExpiresAt).HasColumnType("timestamp with time zone");
        grant.ToTable("CoursePermissions", table =>
        {
            table.HasCheckConstraint("CK_RevisionPermissions_Ids",
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_RevisionPermissions_Action",
                "\"Action\" IN ('revision.create','revision.read','revision.mine','revision.pending.reviews','revision.submit','revision.approve','revision.return')");
        });
        grant.HasKey(value => new { value.UserId, value.CourseId, value.Action });
        grant.Property(value => value.Action).IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
    }
}

public sealed class PostgresRevisionCoursePermissionReader(
    IDbContextFactory<RevisionSecurityDbContext> contexts, TimeProvider clock) : IRevisionCoursePermissionReader
{
    public async ValueTask<bool> IsGrantedAsync(Guid userId, Guid courseId, string action,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || courseId == Guid.Empty || !RevisionActions.IsCourseAction(action))
            return false;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return await db.Permissions.AsNoTracking().AnyAsync(value => value.UserId == userId &&
            value.CourseId == courseId && value.Action == action && value.IsActive &&
            (value.ExpiresAt == null || value.ExpiresAt > now), cancellationToken);
    }
}

public static class RevisionSecurityPersistence
{
    public static IServiceCollection AddRevisionSecurityPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContextFactory<RevisionSecurityDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "revision_security")));
        services.AddSingleton<IRevisionCoursePermissionReader, PostgresRevisionCoursePermissionReader>();
        return services;
    }
}

[DbContext(typeof(RevisionSecurityDbContext))]
[Migration("20261106140000_RevisionCoursePermissions")]
public sealed class RevisionCoursePermissionsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("revision_security");
        migrationBuilder.CreateTable("CoursePermissions", schema: "revision_security", columns: table => new
        {
            UserId = table.Column<Guid>(type: "uuid", nullable: false),
            CourseId = table.Column<Guid>(type: "uuid", nullable: false),
            Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            IsActive = table.Column<bool>(type: "boolean", nullable: false),
            ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CoursePermissions", value => new { value.UserId, value.CourseId, value.Action });
            table.CheckConstraint("CK_RevisionPermissions_Ids",
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.CheckConstraint("CK_RevisionPermissions_Action",
                "\"Action\" IN ('revision.create','revision.read','revision.mine','revision.pending.reviews','revision.submit','revision.approve','revision.return')");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("CoursePermissions", "revision_security");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => RevisionSecurityInitialModel.Build(modelBuilder);
}

[DbContext(typeof(RevisionSecurityDbContext))]
public sealed class RevisionSecurityModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => RevisionSecurityInitialModel.Build(modelBuilder);
}

// Frozen migration metadata. Keep it independent of runtime model configuration so
// a later model change is detected rather than silently changing this snapshot.
internal static class RevisionSecurityInitialModel
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("revision_security")
            .HasAnnotation("ProductVersion", "10.0.14").HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.UseIdentityByDefaultColumns();
        modelBuilder.Entity("Platform.Api.Security.RevisionAuthorization.RevisionCoursePermission", grant =>
        {
            grant.Property<Guid>("UserId").HasColumnType("uuid");
            grant.Property<Guid>("CourseId").HasColumnType("uuid");
            grant.Property<string>("Action").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            grant.Property<bool>("IsActive").HasColumnType("boolean");
            grant.Property<DateTimeOffset?>("ExpiresAt").HasColumnType("timestamp with time zone");
            grant.HasKey("UserId", "CourseId", "Action");
            grant.ToTable("CoursePermissions", "revision_security", table =>
            {
                table.HasCheckConstraint("CK_RevisionPermissions_Ids",
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_RevisionPermissions_Action",
                    "\"Action\" IN ('revision.create','revision.read','revision.mine','revision.pending.reviews','revision.submit','revision.approve','revision.return')");
            });
        });
    }
}
