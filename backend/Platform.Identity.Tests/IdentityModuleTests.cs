using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests;

[TestFixture]
public class IdentityModuleTests
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private PlatformIdentityDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = BuildProvider("Host=localhost;Database=model_only");
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _scope.Dispose();
        _provider.Dispose();
    }

    [Test]
    public void Module_ResolvesContext_WithoutConnectionString()
    {
        using var provider = BuildProvider(connectionString: null);
        using var scope = provider.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>(), Is.Not.Null);
    }

    [Test]
    public void Module_ResolvesSessionStore()
    {
        Assert.That(_scope.ServiceProvider.GetRequiredService<ISessionStore>(), Is.Not.Null);
    }

    [Test]
    public void Accounts_AreMappedToIdentitySchema()
    {
        var account = AccountEntity();

        Assert.That(account.GetSchema(), Is.EqualTo("identity"));
        Assert.That(account.GetTableName(), Is.EqualTo("accounts"));
    }

    [Test]
    public void Email_HasUniqueIndex()
    {
        var index = AccountEntity().GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(UserAccount.Email));

        Assert.That(index.IsUnique, Is.True);
    }

    [Test]
    public void Status_IsStoredAsText()
    {
        var status = AccountEntity().FindProperty(nameof(UserAccount.Status))!;

        Assert.That(status.GetProviderClrType(), Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Role_IsStoredAsString_InAccountsTable()
    {
        var role = AccountEntity().FindProperty(nameof(UserAccount.Role))!;

        Assert.That(role.GetColumnName(), Is.EqualTo("role"));
        Assert.That(role.GetProviderClrType(), Is.EqualTo(typeof(string)));
        Assert.That(role.IsNullable, Is.False);
    }

    [Test]
    public void PasswordHash_IsOptionalColumn()
    {
        var hash = AccountEntity().FindProperty(nameof(UserAccount.PasswordHash))!;

        Assert.That(hash.GetColumnName(), Is.EqualTo("password_hash"));
        Assert.That(hash.IsNullable, Is.True);
    }

    [Test]
    public void Migrations_MatchTheModel()
    {
        Assert.That(_context.Database.HasPendingModelChanges(), Is.False);
    }

    // IDN-06: the MFA purpose is reachable only through ITotpSecretProtector, never through a plain IDataProtector.
    [Test]
    public void Module_RegistersNoPlainDataProtector_OnlyTheTotpSecretProtector()
    {
        Assert.That(_provider.GetService<IDataProtector>(), Is.Null);
        Assert.That(_provider.GetRequiredService<ITotpSecretProtector>(), Is.Not.Null);
    }

    [Test]
    public void TotpSecretProtector_CannotBeReadWithAnotherPurpose_AndCannotReadOtherPurposes()
    {
        var totp = _provider.GetRequiredService<ITotpSecretProtector>();
        var other = _provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("SomeOtherModule");

        var secretCiphertext = totp.Protect("JBSWY3DPEHPK3PXP");
        var otherCiphertext = other.Protect("other module data");

        Assert.That(totp.Unprotect(secretCiphertext), Is.EqualTo("JBSWY3DPEHPK3PXP"));
        Assert.Throws<CryptographicException>(() => other.Unprotect(secretCiphertext));
        Assert.Throws<CryptographicException>(() => totp.Unprotect(otherCiphertext));
    }

    // IDN-06: a restart is a new service provider. With the same KeysPath it still reads the stored secret.
    [Test]
    public void TotpSecretProtector_WithKeysPath_NewInstanceReadsTheStoredSecret()
    {
        // Arrange
        var keysPath = Directory.CreateTempSubdirectory("idn06-keys-").FullName;
        try
        {
            string ciphertext;
            using (var first = BuildProvider("Host=localhost;Database=model_only", keysPath))
                ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

            // Act
            using var second = BuildProvider("Host=localhost;Database=model_only", keysPath);
            var secret = second.GetRequiredService<ITotpSecretProtector>().Unprotect(ciphertext);

            // Assert
            Assert.That(secret, Is.EqualTo("JBSWY3DPEHPK3PXP"));
            Assert.That(Directory.GetFiles(keysPath, "key-*.xml"), Is.Not.Empty);
        }
        finally
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }

    // IDN-06: keys from another folder do not open the secret.
    [Test]
    public void TotpSecretProtector_WithAnotherKeysPath_CannotReadTheStoredSecret()
    {
        // Arrange
        var firstPath = Directory.CreateTempSubdirectory("idn06-keys-").FullName;
        var secondPath = Directory.CreateTempSubdirectory("idn06-keys-").FullName;
        try
        {
            string ciphertext;
            using (var first = BuildProvider("Host=localhost;Database=model_only", firstPath))
                ciphertext = first.GetRequiredService<ITotpSecretProtector>().Protect("JBSWY3DPEHPK3PXP");

            // Act
            using var second = BuildProvider("Host=localhost;Database=model_only", secondPath);
            var protector = second.GetRequiredService<ITotpSecretProtector>();

            // Assert
            Assert.Throws<CryptographicException>(() => protector.Unprotect(ciphertext));
        }
        finally
        {
            Directory.Delete(firstPath, recursive: true);
            Directory.Delete(secondPath, recursive: true);
        }
    }

    private IEntityType AccountEntity() => _context.Model.FindEntityType(typeof(UserAccount))!;

    private static ServiceProvider BuildProvider(string? connectionString, string? keysPath = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = connectionString,
                [IdentityModule.DataProtectionKeysPathKey] = keysPath
            })
            .Build();

        return new ServiceCollection().AddIdentityModule(configuration).BuildServiceProvider();
    }
}
