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
using Microsoft.Extensions.Hosting;
using Moq;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Tests;

/// <summary>
/// IDN-06: the key ring that encrypts TOTP secrets is persistent and encrypted at rest, and outside Development the
/// host does not start when the key ring cannot be used.
/// </summary>
[TestFixture]
[Category("US-22")]
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
        _certificate = CreateCertificate("keyring.pfx");
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

    [Test]
    public void TotpSecretProtector_AnotherKeysPath_CannotReadTheSecret()
    {
        // Arrange
        string ciphertext;
        using (var first = BuildProvider(Settings()))
            ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");
        var settings = Settings();
        settings["Identity:DataProtection:KeysPath"] = Path.Combine(_root, "other-keys");
        using var second = BuildProvider(settings);
        var protector = second.GetRequiredService<ITotpSecretProtector>();

        // Act
        var read = () => protector.Unprotect(ciphertext);

        // Assert
        Assert.That(read, Throws.InstanceOf<CryptographicException>());
    }

    // Replacing the certificate without the old one (IDN-02_TOTP_MFA.md, "Zamena sertifikata"): the existing keys
    // were encrypted with the old certificate, so the stored secrets cannot be read until it is back.
    [Test]
    public void TotpSecretProtector_ReplacedCertificate_CannotReadTheSecretUntilTheOldOneIsBack()
    {
        // Arrange
        string ciphertext;
        using (var first = BuildProvider(Settings()))
            ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");
        var replaced = Settings();
        replaced["Identity:DataProtection:CertificatePath"] = CreateCertificate("replaced.pfx");

        // Act
        using var withReplaced = BuildProvider(replaced);
        var readWithReplaced = () => withReplaced.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext);
        using var withOld = BuildProvider(Settings());
        var readWithOld = withOld.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext);

        // Assert
        Assert.That(readWithReplaced, Throws.InstanceOf<CryptographicException>());
        Assert.That(readWithOld, Is.EqualTo("JBSWY3DPEHPK3PXP"));
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
    public void AddMfaKeyRing_ExistingKeyFileWithoutReadAccess_Throws()
    {
        // Arrange
        // A real key from a first start; then the directory stays readable, but the key file is not, as after a
        // restore with another file owner.
        using (var first = BuildProvider(Settings()))
            first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");
        var keyFile = Directory.GetFiles(_keys, "key-*.xml").Single();
        TakeAwayReadFromFile(keyFile);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => BuildProvider(Settings()));

        // Assert
        Assert.That(error!.Message, Does.Contain("key file that cannot be read"));
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

    [Test]
    public void ApiHost_InProductionWithKeyRing_StartsAndWritesEncryptedKeys()
    {
        // Arrange
        var settings = Settings();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

        // Act
        var protector = factory.Services.GetRequiredService<ITotpSecretProtector>();
        var ciphertext = protector.Protect("JBSWY3DPEHPK3PXP");

        // Assert
        Assert.That(protector.Unprotect(ciphertext), Is.EqualTo("JBSWY3DPEHPK3PXP"));
        Assert.That(Directory.GetFiles(_keys, "key-*.xml"), Is.Not.Empty, "the host uses the configured KeysPath");
    }

    private string CreateCertificate(string fileName)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=MFA key ring test", rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, Password));
        return path;
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

    /// <summary>Takes the read right on one file: Deny ACL rule on Windows, mode bits elsewhere (ignored for root).</summary>
    private void TakeAwayReadFromFile(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            DenyReadOnWindows(file);
            return;
        }

        if (Environment.IsPrivilegedProcess)
            Assert.Ignore("Root ignores file mode bits, so the missing right cannot be simulated.");

        RemoveUnixReadBit(file);
    }

    [UnsupportedOSPlatform("windows")]
    private void RemoveUnixReadBit(string file)
    {
        var before = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, UnixFileMode.UserWrite);
        _restoreAccess = () => File.SetUnixFileMode(file, before);
    }

    [SupportedOSPlatform("windows")]
    private void DenyReadOnWindows(string file)
    {
        var info = new FileInfo(file);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
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
        // AddMfaKeyRing reads only the environment name (IsDevelopment).
        services.AddMfaKeyRing(configuration, Mock.Of<IHostEnvironment>(host => host.EnvironmentName == environment));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
