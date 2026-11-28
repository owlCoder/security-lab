using Microsoft.EntityFrameworkCore;
using Platform.Identity;
using Platform.Identity.Persistence;

namespace Platform.Api.Development;

public static class DevelopmentDatabase
{
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// US-02: applies pending identity migrations when the API starts locally. Other environments never migrate
    /// on startup. Returns false when the database is not ready; the API still starts, as it does without a database.
    /// </summary>
    public static Task<bool> ApplyIdentityMigrationsAsync(this WebApplication app, CancellationToken ct = default) =>
        app.Services.ApplyIdentityMigrationsAsync(ct);

    /// <summary>
    /// The same for the services of a built host (environment, configuration and logging are read from them),
    /// so tests can run it on the host of <c>WebApplicationFactory</c>.
    /// </summary>
    public static async Task<bool> ApplyIdentityMigrationsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        DevelopmentAccountSeeder.RequireDevelopment(services.GetRequiredService<IHostEnvironment>());

        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DevelopmentDatabase));

        if (string.IsNullOrWhiteSpace(services.GetRequiredService<IConfiguration>()
                .GetConnectionString(IdentityModule.ConnectionStringName)))
        {
            logger.LogInformation(
                "Identity connection string is not set, migrations and development accounts are skipped. See docs/DEVELOPMENT.md.");
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DatabaseTimeout);
            using var scope = services.CreateScope();
            var scoped = scope.ServiceProvider;
            // IDN-01: AspNetUsers is in the same identity schema and shares the migration history table.
            await scoped.GetRequiredService<PlatformIdentityDbContext>().Database.MigrateAsync(timeout.Token);
            await scoped.GetRequiredService<AccountDbContext>().Database.MigrateAsync(timeout.Token);

            logger.LogInformation("Identity migrations are up to date.");
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Identity migrations were not applied: {Message} Is the database running? See docs/DEVELOPMENT.md.",
                ex.Message);
            return false;
        }
    }
}
