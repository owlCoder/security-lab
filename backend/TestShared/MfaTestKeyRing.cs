using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace SecurityLab.TestShared;

/// <summary>
/// IDN-06: outside Development the API does not start without a persistent, certificate-protected Data Protection
/// key ring (<c>Identity:DataProtection</c>). Test hosts that run as Production or Testing call
/// <see cref="UseMfaTestKeyRing"/>: a key directory and a self-signed PFX in the temp folder, created once per
/// test process. Linked into each test project from backend/TestShared; not a production setting.
/// </summary>
internal static class MfaTestKeyRing
{
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
        var root = Path.Combine(Path.GetTempPath(), "securitylab-mfa-keyring-" + Guid.NewGuid().ToString("N"));
        var keys = Directory.CreateDirectory(Path.Combine(root, "keys")).FullName;

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SecurityLab MFA test key ring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var certificatePath = Path.Combine(root, "keyring.pfx");
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, password));

        return (keys, certificatePath, password);
    }
}
