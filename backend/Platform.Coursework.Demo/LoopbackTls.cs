using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Platform.Coursework.Demo;

internal static class LoopbackTls
{
    public static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    public static X509Certificate2 CreateServerCertificate()
    {
        // Windows Schannel may reject a server certificate backed by an ephemeral
        // private key. Reimport PKCS#12 with the same key storage mode Kestrel
        // uses for a PFX. No permanent key or trusted root is installed.
        using var generated = CreateCertificate();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pfx = generated.Export(X509ContentType.Pkcs12, password);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.DefaultKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    public static HttpClientHandler CreateHandler(Uri destination, byte[] pin, Action<bool>? observed = null)
    {
        if (destination is not { Scheme: "https", Host: "127.0.0.1" } || pin.Length != 32)
            throw new ArgumentException("A loopback HTTPS address and a SHA-256 certificate pin are required.");
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (request, actual, _, errors) =>
            {
                var accepted = request.RequestUri is { Scheme: "https", Host: "127.0.0.1" } uri &&
                    uri.Port == destination.Port && actual is not null &&
                    (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0 &&
                    DateTime.UtcNow >= actual.NotBefore.ToUniversalTime() && DateTime.UtcNow <= actual.NotAfter.ToUniversalTime() &&
                    CryptographicOperations.FixedTimeEquals(actual.GetCertHash(HashAlgorithmName.SHA256), pin);
                observed?.Invoke(accepted);
                return accepted;
            }
        };
    }
}
