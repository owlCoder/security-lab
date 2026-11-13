using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure;
using Platform.Membership.Infrastructure.Development;
namespace Platform.Membership.Composition;

public static class MembershipDevelopmentSeed
{
    /// <summary>
    /// Samo za Development: upisuje probne nastavnike u probne kurseve, ako već nisu upisani.
    /// Ako baza nije podešena ili migracija nije pokrenuta, samo upozori i nastavi.
    /// </summary>
    public static async Task SeedMembershipDevelopmentDataAsync(
    this IServiceProvider services,
    CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Platform.Membership.DevelopmentSeed");
        if (string.IsNullOrWhiteSpace(
        services.GetRequiredService<IConfiguration>().GetConnectionString(MembershipConnection.Name)))
        {
            logger.LogWarning(
            "ConnectionStrings:{Name} is not set; development memberships were not seeded.",
            MembershipConnection.Name);
            return;
        }
        var store = services.GetRequiredService<IMembershipStore>();
        var clock = services.GetRequiredService<TimeProvider>();
        try
        {
            foreach (var (courseId, teacherId) in DevelopmentMembers.Teachers)
            {
                if (await store.FindActiveAsync(courseId, teacherId, cancellationToken) is null)
                {
                    await store.AddAsync(
                    CourseMember.Enroll(
                    courseId,
                    teacherId,
                    MemberRole.Teacher,
                    DevelopmentMembers.SeedActor,
                    clock.GetUtcNow()),
                    cancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
            exception,
            "Development memberships were not seeded. Is the database running and migrated?");
        }
    }
}