using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        services.AddDbContext<PlatformIdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));

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
        services.AddScoped<ISessionStore, SessionStore>();

        services.AddScoped<BackupCodeStore>();

        services.AddScoped<IBackupCodeStore>(serviceProvider => serviceProvider.GetRequiredService<BackupCodeStore>());

        services.AddScoped<IBackupCodeConsumption>(serviceProvider => serviceProvider.GetRequiredService<BackupCodeStore>());

        services.AddSingleton<IBackupCodeGenerator, BackupCodeGenerator>();

        services.AddScoped<IBackupCodeRedemptionService, BackupCodeRedemptionService>();

        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));
}
