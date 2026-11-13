using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Models;

namespace Platform.Identity.Persistence;

public sealed class PlatformIdentityDbContext(DbContextOptions<PlatformIdentityDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "identity";

    public DbSet<UserAccount> Accounts => Set<UserAccount>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformIdentityDbContext).Assembly);
    }
}
