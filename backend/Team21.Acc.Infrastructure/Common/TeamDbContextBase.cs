using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Common.Interfaces;

namespace Team21.Acc.Infrastructure.Common;

/// <summary>
/// What both our contexts share: own schema, UTC dates, CreatedAt and UpdatedAt on save and the
/// soft delete filter. A context only adds its DbSets and its schema name.
/// </summary>
public abstract class TeamDbContextBase : DbContext, ITeamDbContext
{
    /// <summary>
    /// Name of the soft delete filter, for IgnoreQueryFilters.
    /// </summary>
    public const string SoftDeleteFilter = "SoftDelete";

    protected TeamDbContextBase(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The Postgres schema this context owns, for example "courses".</summary>
    protected abstract string Schema { get; }

    public void ClearChangeTracker() => ChangeTracker.Clear();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Covers DateTime? as well
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        // Both contexts are in this assembly, take only this context's configurations
        // Team21.Acc.Infrastructure.Courses.Data.CoursesDbContext -> Team21.Acc.Infrastructure.Courses
        var contextNamespace = GetType().Namespace!;
        var moduleNamespace = contextNamespace[..contextNamespace.LastIndexOf('.')];
        modelBuilder.ApplyConfigurationsFromAssembly(
            GetType().Assembly,
            type => type.Namespace == moduleNamespace
                || type.Namespace?.StartsWith(moduleNamespace + ".", StringComparison.Ordinal) == true);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            //.Where(e => !e.IsDeleted) Building Logical Expression Tree
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(SoftDeleteFilter, filter);
        }
    }

    // Every SaveChanges and SaveChangesAsync overload ends up in one of these two
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added && entry.Entity is IEntity entity)
            {
                entity.CreatedAt = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified && entry.Entity is IUpdatable updatable)
            {
                updatable.UpdatedAt = DateTime.UtcNow;
            }

            // Written once on insert, an update puts them back
            if (entry.State == EntityState.Modified)
            {
                foreach (var name in CreateOnlyProperties)
                {
                    if (entry.Metadata.FindProperty(name) is null)
                        continue;

                    var property = entry.Property(name);
                    property.CurrentValue = property.OriginalValue;
                    property.IsModified = false;
                }
            }
        }
    }

    private static readonly string[] CreateOnlyProperties = [nameof(IEntity.CreatedAt), nameof(BaseEntity.CreatedBy)];
}
