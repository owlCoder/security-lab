using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Platform.Identity.Data;

public sealed class PlatformIdentityDbContextFactory : IDesignTimeDbContextFactory<PlatformIdentityDbContext>
{
    public PlatformIdentityDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddLocalEnvFile()
            .AddUserSecrets<PlatformIdentityDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(IdentityModule.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{IdentityModule.ConnectionStringName}' not found. " +
                "Copy .env.example to .env, see docs/DEVELOPMENT.md.");

        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>();
        options.UseIdentityNpgsql(connectionString);

        return new PlatformIdentityDbContext(options.Options);
    }
}
