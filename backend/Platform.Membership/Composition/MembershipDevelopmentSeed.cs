using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure;
using Platform.Membership.Infrastructure.Development;
using Platform.Membership.Infrastructure.Persistence;

namespace Platform.Membership.Composition;

public static class MembershipDevelopmentSeed
{
    /// <summary>
    /// Samo za Development: primeni membership i audit migracije (upis i audit su jedna
    /// transakcija, pa audit šema mora biti u istoj bazi), pa upiši probne nastavnike.
    /// Ako baza nije podešena, samo upozori i nastavi.
    /// </summary>
    public static async Task SeedMembershipDevelopmentDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Platform.Membership.DevelopmentSeed");
        var connectionString = services.GetRequiredService<IConfiguration>()
            .GetConnectionString(MembershipConnection.Name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("ConnectionStrings:{Name} is not set; development memberships were not seeded.",
                MembershipConnection.Name);
            return;
        }

        var store = services.GetRequiredService<IMembershipStore>();
        var clock = services.GetRequiredService<TimeProvider>();
        var seedAudit = new AuditContext(AuditActor.ForUser(DevelopmentMembers.SeedActor), "membership-dev-seed");

        try
        {
            var contexts = services.GetRequiredService<IDbContextFactory<MembershipDbContext>>();
            await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
            {
                await db.Database.MigrateAsync(cancellationToken);
            }

            await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(connectionString, cancellationToken);

            foreach (var (courseId, teacherId) in DevelopmentMembers.Teachers)
            {
                if (await store.FindActiveAsync(courseId, teacherId, cancellationToken) is null)
                {
                    await store.AddAsync(
                        CourseMember.Enroll(courseId, teacherId, MemberRole.Teacher,
                            DevelopmentMembers.SeedActor, clock.GetUtcNow()),
                        seedAudit,
                        cancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Development memberships were not seeded. Is the database running?");
        }
    }
}