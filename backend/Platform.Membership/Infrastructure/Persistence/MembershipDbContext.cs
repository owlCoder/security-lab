
using Microsoft.EntityFrameworkCore;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

/// <summary>
/// Baza modula za članstvo (ACC-02, ACC-07). Ista PostgreSQL baza kao ostali moduli,
/// ali sopstvena šema i sopstvene migracije.
/// </summary>
public sealed class MembershipDbContext(DbContextOptions<MembershipDbContext> options) : DbContext(options)
{
    public const string Schema = "membership";

    public DbSet<CourseMember> CourseMembers => Set<CourseMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MembershipDbContext).Assembly);
    }
}
