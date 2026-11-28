using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SecurityLab.TestShared;

/// <summary>
/// US-22 (IDN-06): outside Development the API does not start without a persistent, certificate-protected Data
/// Protection key ring (<c>Identity:DataProtection</c>). backend/Directory.Build.props compiles this file into every
/// test project; its module initializer sets <c>Identity__DataProtection__*</c> environment variables once per test
/// process, before any host is built, so test hosts that run as Production or Testing need no call of their own.
/// The key directory and the self-signed PFX are in the temp folder and are deleted when the process exits; folders
/// left by a killed process are deleted by a later run once they are a day old. Variables that are already set (by
/// CI or a developer) are left as they are. Not a production setting.
/// </summary>
internal static class MfaTestKeyRing
{
    public const string KeysPathVariable = "Identity__DataProtection__KeysPath";
    public const string CertificatePathVariable = "Identity__DataProtection__CertificatePath";
    public const string CertificatePasswordVariable = "Identity__DataProtection__CertificatePassword";

    private const string FolderPrefix = "securitylab-mfa-keyring-";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

#pragma warning disable CA2255 // A module initializer is the point: it runs before any test builds a host.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void SetForTestProcess()
    {
        if (Environment.GetEnvironmentVariable(KeysPathVariable) is not null
            || Environment.GetEnvironmentVariable(CertificatePathVariable) is not null)
            return;

        var (keysPath, certificatePath, password) = Create();
        Environment.SetEnvironmentVariable(KeysPathVariable, keysPath);
        Environment.SetEnvironmentVariable(CertificatePathVariable, certificatePath);
        Environment.SetEnvironmentVariable(CertificatePasswordVariable, password);
    }

    private static (string KeysPath, string CertificatePath, string Password) Create()
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
