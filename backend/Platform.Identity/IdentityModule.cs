using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Data;
using Platform.Identity.Services;

namespace Platform.Identity;

/// <summary>Single entry point for Program.cs: builder.Services.AddIdentityModule(builder.Configuration).</summary>
public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        services.AddDbContext<PlatformIdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<ICredentialStore, EfCredentialStore>();
        services.AddScoped<ICredentialVerifier, CredentialVerifier>();
        services.AddScoped<ISessionStore, SessionStore>();

        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));
}
