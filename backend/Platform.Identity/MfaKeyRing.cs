using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Platform.Identity;

/// <summary>
/// IDN-06: where the Data Protection key ring that encrypts TOTP secrets is kept, and the certificate that encrypts
/// the keys at rest. Losing the key ring makes every stored TOTP secret unreadable, so outside Development both
/// values are required and the host does not start without them. Secrets (the certificate password) come from
/// environment variables or user-secrets, never from the repository (ADM-04).
/// </summary>
public sealed class MfaKeyRingOptions
{
    public const string Section = "Identity:DataProtection";

    /// <summary>Directory for the key ring. Must survive restarts and be part of the backup with the database.</summary>
    public string? KeysPath { get; set; }

    /// <summary>PFX file with a private key; it encrypts every key written to <see cref="KeysPath"/>.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeysPath) && !string.IsNullOrWhiteSpace(CertificatePath);
}

public static class MfaKeyRingRegistration
{
    /// <summary>
    /// Persists the key ring to <see cref="MfaKeyRingOptions.KeysPath"/> and encrypts it with the certificate.
    /// Outside Development missing or broken settings throw, so the host does not start. Development without any
    /// setting keeps the default local key ring.
    /// </summary>
    public static IServiceCollection AddMfaKeyRing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(MfaKeyRingOptions.Section).Get<MfaKeyRingOptions>()
                      ?? new MfaKeyRingOptions();

        var error = Validate(options, environment);
        if (error is not null)
            throw new InvalidOperationException(error);

        if (!options.IsConfigured)
            return services;

        services.AddDataProtection()
            .SetApplicationName("SecurityLab")
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeysPath!))
            .ProtectKeysWithCertificate(LoadCertificate(options));
        return services;
    }

    /// <summary>Returns null when the settings can be used, otherwise a message without secrets.</summary>
    public static string? Validate(MfaKeyRingOptions options, IHostEnvironment environment)
    {
        if (!options.IsConfigured)
        {
            if (environment.IsDevelopment()
                && string.IsNullOrWhiteSpace(options.KeysPath)
                && string.IsNullOrWhiteSpace(options.CertificatePath))
                return null;

            return $"{MfaKeyRingOptions.Section}:KeysPath and {MfaKeyRingOptions.Section}:CertificatePath must both " +
                   "be set outside Development. Without a persistent, encrypted key ring every TOTP secret is lost " +
                   "on restart or restore, and every account with MFA is locked out.";
        }

        if (!File.Exists(options.CertificatePath))
            return $"{MfaKeyRingOptions.Section}:CertificatePath does not point to a file.";

        try
        {
            using var certificate = LoadCertificate(options);
            if (!certificate.HasPrivateKey)
                return $"{MfaKeyRingOptions.Section}:CertificatePath must contain a private key to decrypt the key ring.";
        }
        catch (CryptographicException)
        {
            // No exception text: it can name the file or hint at the password.
            return $"{MfaKeyRingOptions.Section}: the certificate cannot be loaded (wrong file or password).";
        }

        try
        {
            Directory.CreateDirectory(options.KeysPath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{MfaKeyRingOptions.Section}:KeysPath cannot be created.";
        }

        return null;
    }

    private static X509Certificate2 LoadCertificate(MfaKeyRingOptions options) =>
        X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, options.CertificatePassword);
}
