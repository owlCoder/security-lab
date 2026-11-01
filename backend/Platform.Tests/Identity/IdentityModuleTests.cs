using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity;
using Platform.Identity.Data;
using Platform.Identity.Domain;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("ADM-02")]
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

    private IEntityType AccountEntity() => _context.Model.FindEntityType(typeof(UserAccount))!;

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = connectionString
            })
            .Build();

        return new ServiceCollection().AddIdentityModule(configuration).BuildServiceProvider();
    }
}
