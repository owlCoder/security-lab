using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace SecurityLab.TestShared;

/// <summary>
/// IDN-06: outside Development the API does not start without a persistent, certificate-protected Data Protection
/// key ring (<c>Identity:DataProtection</c>). Test hosts that run as Production or Testing call
/// <see cref="UseMfaTestKeyRing"/>: a key directory and a self-signed PFX in the temp folder, created once per
/// test process and deleted when the process exits. Folders left by a process that was killed are deleted by a
/// later run once they are a day old. Linked into each test project from backend/TestShared; not a production
/// setting.
/// </summary>
internal static class MfaTestKeyRing
{
    private const string FolderPrefix = "securitylab-mfa-keyring-";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);
    private static readonly Lazy<(string KeysPath, string CertificatePath, string Password)> Files = new(Create);

    public static IWebHostBuilder UseMfaTestKeyRing(this IWebHostBuilder builder)
    {
        var (keysPath, certificatePath, password) = Files.Value;
        return builder
            .UseSetting("Identity:DataProtection:KeysPath", keysPath)
            .UseSetting("Identity:DataProtection:CertificatePath", certificatePath)
            .UseSetting("Identity:DataProtection:CertificatePassword", password);
    }

    private static (string, string, string) Create()
    {
        DeleteStaleFolders();
        var root = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N"));
        var keys = Directory.CreateDirectory(Path.Combine(root, "keys")).FullName;
        // The PFX holds a test private key; it should not pile up on developer machines.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(root);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SecurityLab MFA test key ring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var certificatePath = Path.Combine(root, "keyring.pfx");
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, password));

        return (keys, certificatePath, password);
    }

    // Only this helper's own folders, and only old ones: a parallel test process may still use a recent one.
    private static void DeleteStaleFolders()
    {
        foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), FolderPrefix + "*"))
            if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) > StaleAfter)
                TryDelete(folder);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup only; a folder still in use is removed by a later run.
        }
    }
}
