using Microsoft.EntityFrameworkCore;
using Platform.Identity.Domain;

namespace Platform.Identity.Data;

public sealed class PlatformIdentityDbContext(DbContextOptions<PlatformIdentityDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "identity";

    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformIdentityDbContext).Assembly);
    }
}
