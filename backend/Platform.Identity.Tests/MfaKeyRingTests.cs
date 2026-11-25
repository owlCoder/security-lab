using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Tests;

/// <summary>
/// milanPR15 review point 1: the key ring that encrypts TOTP secrets is persistent and encrypted at rest, and the
/// host does not start outside Development without it.
/// </summary>
[TestFixture]
[Category("US-17")]
public sealed class MfaKeyRingTests
{
    private string _root = null!;
    private string _keys = null!;
    private string _certificate = null!;
    private const string Password = "test-only-password";

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("mfa-keyring-tests-").FullName;
        _keys = Path.Combine(_root, "keys");
        _certificate = Path.Combine(_root, "keyring.pfx");
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=MFA key ring test", rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(_certificate, certificate.Export(X509ContentType.Pfx, Password));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void SecretProtectedByOneInstance_IsReadByANewInstanceWithTheSameKeyRing()
    {
        string ciphertext;
        using (var first = BuildProvider(Settings()))
            ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

        using var second = BuildProvider(Settings());

        Assert.That(second.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext), Is.EqualTo("JBSWY3DPEHPK3PXP"));
    }

    [Test]
    public void KeysOnDisk_AreEncryptedWithTheCertificate()
    {
        using (var provider = BuildProvider(Settings()))
            provider.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

        var keyFiles = Directory.GetFiles(_keys, "key-*.xml");
        Assert.That(keyFiles, Is.Not.Empty, "the key ring is written to KeysPath");
        foreach (var file in keyFiles)
        {
            var xml = File.ReadAllText(file);
            Assert.That(xml, Does.Contain("encryptedSecret").And.Contain("EncryptedXmlDecryptor"));
            Assert.That(xml, Does.Not.Contain("<masterKey"), "no plain master key on disk");
        }
    }

    [Test]
    public void WithoutTheCertificate_TheStoredKeysCannotBeUsed()
    {
        string ciphertext;
        using (var provider = BuildProvider(Settings()))
            ciphertext = provider.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

        // Same key files, no certificate: a copy of KeysPath alone does not open the secrets.
        using var withoutCertificate = BuildProvider(new Dictionary<string, string?>(), Environments.Development,
            configure: services => services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_keys)));

        Assert.Throws<CryptographicException>(() =>
            withoutCertificate.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext));
    }

    [TestCase("Production")]
    [TestCase("Testing")]
    [TestCase("Staging")]
    public void OutsideDevelopment_MissingSettings_StopTheStart(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(new Dictionary<string, string?>(), environment));

        Assert.That(error!.Message, Does.Contain("Identity:DataProtection:KeysPath"));
    }

    [Test]
    public void InDevelopment_NoSettings_KeepTheDefaultKeyRing()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>(), Environments.Development);

        Assert.That(provider.GetRequiredService<ITotpSecretProtector>().Protect("x"), Is.Not.Empty);
    }

    [Test]
    public void OnlyOneOfTheTwoSettings_StopsTheStart_EvenInDevelopment()
    {
        var settings = new Dictionary<string, string?> { ["Identity:DataProtection:KeysPath"] = _keys };

        Assert.Throws<InvalidOperationException>(() => BuildProvider(settings, Environments.Development));
    }

    [Test]
    public void WrongCertificatePassword_StopsTheStart_WithoutRevealingIt()
    {
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePassword"] = "wrong-password-value";

        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.That(error!.Message, Does.Not.Contain("wrong-password-value").And.Not.Contain(Password));
    }

    [Test]
    public void MissingCertificateFile_StopsTheStart()
    {
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePath"] = Path.Combine(_root, "missing.pfx");

        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.That(error!.Message, Does.Contain("does not point to a file"));
    }

    [Test]
    public void CertificateWithoutPrivateKey_StopsTheStart()
    {
        // Same certificate, exported without its private key: it could encrypt new keys but never decrypt them.
        using var full = X509CertificateLoader.LoadPkcs12FromFile(_certificate, Password);
        using var publicOnly = X509CertificateLoader.LoadCertificate(full.RawData);
        var path = Path.Combine(_root, "public-only.pfx");
        File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx, Password));
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePath"] = path;

        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.That(error!.Message, Does.Contain("must contain a private key"));
    }

    [Test]
    public void KeysPathThatCannotBeCreated_StopsTheStart()
    {
        // A directory cannot be created under a regular file, on any OS and for any user.
        var settings = Settings();
        settings["Identity:DataProtection:KeysPath"] = Path.Combine(_certificate, "keys");

        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.That(error!.Message, Does.Contain("KeysPath cannot be created"));
    }

    // The real host: Program.cs registers the key ring, so a Production start without settings fails.
    [Test]
    public void ApiHost_InProductionWithoutKeyRing_DoesNotStart()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        var error = Assert.Catch(() => _ = factory.Services);

        Assert.That(error!.ToString(), Does.Contain("Identity:DataProtection:KeysPath"));
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["Identity:DataProtection:KeysPath"] = _keys,
        ["Identity:DataProtection:CertificatePath"] = _certificate,
        ["Identity:DataProtection:CertificatePassword"] = Password
    };

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        string environment = "Production",
        Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityModule(configuration);
        services.AddMfaKeyRing(configuration, new TestEnvironment(environment));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "SecurityLab.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
