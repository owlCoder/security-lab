using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Identity.Options.Mfa;

namespace Platform.Identity.Composition;

/// <summary>US-22 (IDN-06): the persistent, certificate-protected key ring for TOTP secrets (<see cref="MfaKeyRingOptions"/>).</summary>
public static class MfaKeyRingRegistration
{
    /// <summary>Changing it makes every stored TOTP secret unreadable: Data Protection isolates keys per name.</summary>
    public const string ApplicationName = "SecurityLab";

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
            .SetApplicationName(ApplicationName)
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

            // XML encryption with a certificate needs an RSA key. An ECDSA PFX would pass the check above and fail
            // only at the first enrollment.
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is null)
                return $"{MfaKeyRingOptions.Section}:CertificatePath must hold an RSA key; XML encryption of the key " +
                       "ring does not work with ECDSA or other key types.";
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

        // The directory can already exist with the wrong rights. Data Protection reads the key ring on first use
        // and writes the first key then and a new key about every 90 days, so both rights are checked here,
        // at start, and not left to the first enrollment.
        if (!CanRead(options.KeysPath!))
            return $"{MfaKeyRingOptions.Section}:KeysPath is not readable. Data Protection reads the key ring " +
                   "from it on first use.";

        if (!CanReadEveryKey(options.KeysPath!))
            return $"{MfaKeyRingOptions.Section}:KeysPath holds a key file that cannot be read, for example after a " +
                   "restore with another file owner. Data Protection needs every key to decrypt stored secrets.";

        // A key written before the certificate was set (KeysPath alone, as in #232) keeps its master key in plain
        // text, and Data Protection goes on using it until it expires instead of writing an encrypted one.
        switch (FindKeyFileProblem(options.KeysPath!))
        {
            case KeyFileProblem.NotCertificateEncrypted:
                return $"{MfaKeyRingOptions.Section}:KeysPath holds a key that is not encrypted with a certificate, " +
                       "for example one written before CertificatePath was set. Start from an empty KeysPath; " +
                       "secrets stored with the old key have to be enrolled again.";
            case KeyFileProblem.NotAKeyFile:
                return $"{MfaKeyRingOptions.Section}:KeysPath holds an *.xml file that is not a valid key file, for " +
                       "example damaged by a restore. Restore it from the backup or remove it.";
        }

        if (!CanWrite(options.KeysPath!))
            return $"{MfaKeyRingOptions.Section}:KeysPath is not writable. Data Protection writes the first key " +
                   "on first use and a new key about every 90 days.";

        return null;
    }

    private enum KeyFileProblem
    {
        None,
        NotCertificateEncrypted,
        NotAKeyFile
    }

    /// <summary>
    /// Every <c>&lt;key&gt;</c> file must have its secret in an <c>encryptedSecret</c> made by the certificate
    /// (<c>EncryptedXmlDecryptor</c>) and no plain <c>masterKey</c>. Revocation files hold no secret and are skipped.
    /// A file that cannot be parsed is reported on its own, so a damaged restore is not taken for a missing certificate.
    /// </summary>
    private static KeyFileProblem FindKeyFileProblem(string directory)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        foreach (var file in Directory.EnumerateFiles(directory, "*.xml"))
        {
            XDocument document;
            try
            {
                using var reader = XmlReader.Create(file, settings);
                document = XDocument.Load(reader);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                return KeyFileProblem.NotAKeyFile;
            }

            if (document.Root?.Name.LocalName != "key")
                continue;

            var elements = document.Root.Descendants().ToList();
            var plainMasterKey = elements.Any(e => e.Name.LocalName == "masterKey");
            var encryptedByCertificate = elements.Any(e =>
                e.Name.LocalName == "encryptedSecret"
                && ((string?)e.Attribute("decryptorType"))?.Contains("EncryptedXmlDecryptor", StringComparison.Ordinal) == true);
            if (plainMasterKey || !encryptedByCertificate)
                return KeyFileProblem.NotCertificateEncrypted;
        }

        return KeyFileProblem.None;
    }

    private static bool CanRead(string directory)
    {
        try
        {
            // Only *.xml files are keys; enumerating opens the directory for reading.
            _ = Directory.EnumerateFiles(directory, "*.xml").Any();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanReadEveryKey(string directory)
    {
        try
        {
            // Opening is enough: the content is decrypted later by Data Protection, with the certificate.
            foreach (var key in Directory.EnumerateFiles(directory, "*.xml"))
                using (File.OpenRead(key))
                {
                }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanWrite(string directory)
    {
        // A real write is the only check that works the same on Windows (ACLs), Linux and macOS (mode bits,
        // read-only mounts). The probe is not *.xml, so Data Protection never reads it, and it is deleted on close.
        var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static X509Certificate2 LoadCertificate(MfaKeyRingOptions options) =>
        X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, options.CertificatePassword);
}
