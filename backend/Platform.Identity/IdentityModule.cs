using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity;

/// <summary>Single entry point for Program.cs: builder.Services.AddIdentityModule(builder.Configuration).</summary>
public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        services.TryAddScoped<NpgsqlConnection>(_ =>
            new NpgsqlConnection(connectionString ?? string.Empty));

        services.AddDbContext<PlatformIdentityDbContext>((sp, options) =>
            options.UseIdentityNpgsql(sp.GetRequiredService<NpgsqlConnection>()));

        // IDN-05: an invalid lockout setting stops the service from starting.
        services.AddOptions<IdentityLockoutOptions>()
            .Bind(configuration.GetSection(IdentityLockoutOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityLockoutOptions>, IdentityLockoutOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.TryAddSingleton<IResetTokenGenerator, ResetTokenGenerator>();
        services.AddScoped<ICredentialStore, EfCredentialStore>();
        services.AddScoped<IAccountStatusReader, EfAccountStatusReader>();
        services.AddScoped<ICredentialVerifier, CredentialVerifier>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddScoped<ISessionNotificationPreferences, SessionNotificationPreferences>();
        services.AddScoped<ISessionStore, SessionStore>();

        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, NpgsqlConnection connection) =>
        options.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));
}
