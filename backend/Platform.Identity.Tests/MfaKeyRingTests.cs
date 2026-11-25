using Microsoft.AspNetCore.DataProtection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Tests;

/// <summary>
/// IDN-06: the key ring that encrypts TOTP secrets is persistent and encrypted at rest, and outside Development the
/// host does not start when the key ring cannot be used.
/// </summary>
[TestFixture]
[Category("US-17")]
public sealed class MfaKeyRingTests
{
    private string _root = null!;
    private string _keys = null!;
    private string _certificate = null!;
    private Action? _restoreAccess;
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
    public void TearDown()
    {
        // Rights taken away by a test come back first, otherwise the directory cannot be deleted.
        _restoreAccess?.Invoke();
        _restoreAccess = null;
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void TotpSecretProtector_NewInstanceWithTheSameKeyRing_ReadsTheSecret()
    {
        // Arrange
        string ciphertext;
        using (var first = BuildProvider(Settings()))
            ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");
        using var second = BuildProvider(Settings());

        // Act
        var secret = second.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext);

        // Assert
        Assert.That(secret, Is.EqualTo("JBSWY3DPEHPK3PXP"));
    }

    [Test]
    public void AddMfaKeyRing_FirstProtect_WritesKeysEncryptedWithTheCertificate()
    {
        // Arrange
        using var provider = BuildProvider(Settings());

        // Act
        provider.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

        // Assert
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
    public void TotpSecretProtector_KeysWithoutTheCertificate_CannotReadTheSecret()
    {
        // Arrange
        string ciphertext;
        using (var provider = BuildProvider(Settings()))
            ciphertext = provider.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");
        // Same key files, no certificate: a copy of KeysPath alone does not open the secrets.
        using var withoutCertificate = BuildProvider(new Dictionary<string, string?>(), Environments.Development,
            configure: services => services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_keys)));
        var protector = withoutCertificate.GetRequiredService<ITotpSecretProtector>();

        // Act
        var read = () => protector.Unprotect(ciphertext);

        // Assert
        Assert.That(read, Throws.InstanceOf<CryptographicException>());
    }

    [TestCase("Production")]
    [TestCase("Testing")]
    [TestCase("Staging")]
    public void AddMfaKeyRing_OutsideDevelopmentWithoutSettings_Throws(string environment)
    {
        // Arrange
        var settings = new Dictionary<string, string?>();

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings, environment));

        // Assert
        Assert.That(error!.Message, Does.Contain("Identity:DataProtection:KeysPath"));
    }

    [Test]
    public void AddMfaKeyRing_InDevelopmentWithoutSettings_KeepsTheDefaultKeyRing()
    {
        // Arrange
        using var provider = BuildProvider(new Dictionary<string, string?>(), Environments.Development);

        // Act
        var ciphertext = provider.GetRequiredService<ITotpSecretProtector>().Protect("x");

        // Assert
        Assert.That(ciphertext, Is.Not.Empty);
    }

    [Test]
    public void AddMfaKeyRing_OnlyOneSettingInDevelopment_Throws()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { ["Identity:DataProtection:KeysPath"] = _keys };

        // Act
        var build = () => BuildProvider(settings, Environments.Development);

        // Assert
        Assert.That(build, Throws.InvalidOperationException);
    }

    [Test]
    public void AddMfaKeyRing_WrongCertificatePassword_ThrowsWithoutRevealingIt()
    {
        // Arrange
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePassword"] = "wrong-password-value";

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        // Assert
        Assert.That(error!.Message, Does.Not.Contain("wrong-password-value").And.Not.Contain(Password));
    }

    [Test]
    public void AddMfaKeyRing_MissingCertificateFile_Throws()
    {
        // Arrange
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePath"] = Path.Combine(_root, "missing.pfx");

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        // Assert
        Assert.That(error!.Message, Does.Contain("does not point to a file"));
    }

    [Test]
    public void AddMfaKeyRing_CertificateWithoutPrivateKey_Throws()
    {
        // Arrange
        // Same certificate, exported without its private key: it could encrypt new keys but never decrypt them.
        using var full = X509CertificateLoader.LoadPkcs12FromFile(_certificate, Password);
        using var publicOnly = X509CertificateLoader.LoadCertificate(full.RawData);
        var path = Path.Combine(_root, "public-only.pfx");
        File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx, Password));
        var settings = Settings();
        settings["Identity:DataProtection:CertificatePath"] = path;

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        // Assert
        Assert.That(error!.Message, Does.Contain("must contain a private key"));
    }

    [Test]
    public void AddMfaKeyRing_KeysPathThatCannotBeCreated_Throws()
    {
        // Arrange
        // A directory cannot be created under a regular file, on any OS and for any user.
        var settings = Settings();
        settings["Identity:DataProtection:KeysPath"] = Path.Combine(_certificate, "keys");

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        // Assert
        Assert.That(error!.Message, Does.Contain("KeysPath cannot be created"));
    }

    [Test]
    public void AddMfaKeyRing_ExistingKeysPathWithoutWriteAccess_Throws()
    {
        // Arrange
        // The directory exists, so creating it succeeds; only a real write shows the missing right.
        Directory.CreateDirectory(_keys);
        TakeAwayAccess(_keys, write: true);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(Settings()));

        // Assert
        Assert.That(error!.Message, Does.Contain("KeysPath is not writable"));
    }

    [Test]
    public void AddMfaKeyRing_ExistingKeysPathWithoutReadAccess_Throws()
    {
        // Arrange
        Directory.CreateDirectory(_keys);
        TakeAwayAccess(_keys, write: false);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(Settings()));

        // Assert
        Assert.That(error!.Message, Does.Contain("KeysPath is not readable"));
    }

    [Test]
    public void AddMfaKeyRing_WritableKeysPath_LeavesNoProbeFile()
    {
        // Arrange
        Directory.CreateDirectory(_keys);

        // Act
        using var provider = BuildProvider(Settings());

        // Assert
        Assert.That(Directory.GetFileSystemEntries(_keys), Is.Empty);
    }

    // The real host: Program.cs registers the key ring, so a Production start without settings fails.
    [Test]
    public void ApiHost_InProductionWithoutKeyRing_DoesNotStart()
    {
        // Arrange
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        // Act
        var error = Assert.Catch(() => _ = factory.Services);

        // Assert
        Assert.That(error!.ToString(), Does.Contain("Identity:DataProtection:KeysPath"));
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["Identity:DataProtection:KeysPath"] = _keys,
        ["Identity:DataProtection:CertificatePath"] = _certificate,
        ["Identity:DataProtection:CertificatePassword"] = Password
    };

    /// <summary>
    /// Takes the write or the read right on <paramref name="directory"/> from the current user: a Deny ACL rule on
    /// Windows, mode bits on Linux and macOS. Mode bits do not bind root, so there the test is ignored.
    /// </summary>
    private void TakeAwayAccess(string directory, bool write)
    {
        if (OperatingSystem.IsWindows())
        {
            DenyOnWindows(directory, write);
            return;
        }

        if (Environment.IsPrivilegedProcess)
            Assert.Ignore("Root ignores directory mode bits, so the missing right cannot be simulated.");

        RemoveUnixModeBits(directory, write);
    }

    [UnsupportedOSPlatform("windows")]
    private void RemoveUnixModeBits(string directory, bool write)
    {
        var full = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.SetUnixFileMode(directory, write
            ? UnixFileMode.UserRead | UnixFileMode.UserExecute
            : UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _restoreAccess = () => File.SetUnixFileMode(directory, full);
    }

    [SupportedOSPlatform("windows")]
    private void DenyOnWindows(string directory, bool write)
    {
        var info = new DirectoryInfo(directory);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            write
                ? FileSystemRights.CreateFiles | FileSystemRights.AppendData
                : FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        _restoreAccess = () =>
        {
            var restored = info.GetAccessControl();
            restored.RemoveAccessRuleSpecific(rule);
            info.SetAccessControl(restored);
        };
    }

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
