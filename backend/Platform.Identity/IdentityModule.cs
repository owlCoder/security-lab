using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Data;

namespace Platform.Identity;

/// <summary>Single entry point for Program.cs: builder.Services.AddIdentityModule(builder.Configuration).</summary>
public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        services.AddDbContext<PlatformIdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));

        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));
}
