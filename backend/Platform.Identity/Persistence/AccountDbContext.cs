using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;

namespace Platform.Identity.Persistence;

public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("identity");

        // IDN-01: jedinstven identifikator (email) na nivou baze, ne samo u kodu.
        builder.Entity<AppUser>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique();
    }
}