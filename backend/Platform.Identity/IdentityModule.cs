using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Persistence;
using Platform.Identity.Persistence.Mfa;
using Platform.Identity.Services;

namespace Platform.Identity;

/// <summary>Single entry point for Program.cs: builder.Services.AddIdentityModule(builder.Configuration).</summary>
public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";

    /// <summary>IDN-06: folder of the Data Protection keys that encrypt the TOTP secrets.</summary>
    public const string DataProtectionKeysPathKey = "Identity:DataProtection:KeysPath";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        services.TryAddScoped<NpgsqlConnection>(_ =>
            new NpgsqlConnection(connectionString ?? string.Empty));

        services.AddDbContext<PlatformIdentityDbContext>((sp, options) =>
            options.UseIdentityNpgsql(sp.GetRequiredService<NpgsqlConnection>()));

        // IDN-06: with KeysPath the keys survive a restart and can be backed up with the database. Without it the
        // framework default location is used. The keys are not encrypted at rest (IDN-02_TOTP_MFA.md, key storage).
        var dataProtection = services.AddDataProtection().SetApplicationName("SecurityLab");
        var keysPath = configuration[DataProtectionKeysPathKey];
        if (!string.IsNullOrWhiteSpace(keysPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        // IDN-06: no plain IDataProtector is registered; only MFA gets the MFA purpose.
        services.AddSingleton<ITotpSecretProtector, DataProtectionTotpSecretProtector>();

        // IDN-05: an invalid lockout setting stops the service from starting.
        services.AddOptions<IdentityLockoutOptions>()
            .Bind(configuration.GetSection(IdentityLockoutOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityLockoutOptions>, IdentityLockoutOptionsValidator>();

        // US-18: the MFA lockout has its own section and stricter limits; an invalid value stops the start.
        services.AddOptions<MfaLockoutOptions>()
            .Bind(configuration.GetSection(MfaLockoutOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MfaLockoutOptions>, MfaLockoutOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.TryAddSingleton<IResetTokenGenerator, ResetTokenGenerator>();
        services.AddScoped<ICredentialStore, EfCredentialStore>();
        services.AddScoped<IAccountStatusReader, EfAccountStatusReader>();
        services.AddScoped<ICredentialVerifier, CredentialVerifier>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddScoped<ISessionNotificationPreferences, SessionNotificationPreferences>();
        services.AddScoped<ISessionStore, SessionStore>();
        services.AddScoped<IMfaCredentialsStore, EfMfaCredentialsStore>();
        services.AddScoped<IExternalIdentityLinker, ExternalIdentityLinker>();

        return services;
    }

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, NpgsqlConnection connection) =>
        options.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));

    internal static DbContextOptionsBuilder UseIdentityNpgsql(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformIdentityDbContext.SchemaName));
}
