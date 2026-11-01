using Microsoft.EntityFrameworkCore;
using Platform.Identity;
using Platform.Identity.Data;

namespace Platform.Api.Services.Identity;

public static class DevelopmentDatabase
{
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// US-02: applies pending identity migrations when the API starts locally. Other environments never migrate
    /// on startup. Returns false when the database is not ready; the API still starts, as it does without a database.
    /// </summary>
    public static async Task<bool> ApplyIdentityMigrationsAsync(this WebApplication app, CancellationToken ct = default)
    {
        DevelopmentAccountSeeder.RequireDevelopment(app.Environment);

        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DevelopmentDatabase));

        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(IdentityModule.ConnectionStringName)))
        {
            logger.LogInformation(
                "Identity connection string is not set, migrations and development accounts are skipped. See docs/DEVELOPMENT.md.");
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DatabaseTimeout);
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            await context.Database.MigrateAsync(timeout.Token);

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
