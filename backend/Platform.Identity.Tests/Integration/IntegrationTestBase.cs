using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

[TestFixture]
public abstract class IntegrationTestBase
{
    #region Properties
    protected static CustomWebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected static HttpClient Client { get; private set; } = null!;
    #endregion
    
    [OneTimeSetUp]
    public void BaseClassSetup()
    {
        Factory = new CustomWebApplicationFactory<Program>();
        Client = Factory.CreateClient();
    }

    [OneTimeTearDown]
    public void BaseClassTeardown()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    [SetUp]
    public async Task BaseSetUp()
    {
        // Respawn sam automatski čisti sve tabele, bez obzira ko ih je i kada dodao
        await TestDatabaseFixture.ResetDatabaseAsync();
        // Skida kolac iy header-a pre svakog testa
        Client.DefaultRequestHeaders.Remove("Cookie");
    }

    #region Helper methods
    // Insert u bazu
    protected static async Task InsertAsync<TEntity>(TEntity entity) where TEntity : class
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        db.Set<TEntity>().Add(entity);
        await db.SaveChangesAsync();
    }
    // Citanje iz baze
    protected static async Task<TEntity?> GetAsync<TEntity>(Guid id) where TEntity : class
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var entity = await db.Set<TEntity>().FindAsync(id);
        return entity;
    }
    // Izvlacenje servisa iz applikacije
    protected static TService GetService<TService>() where TService : notnull
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TService>();
    }
    #endregion
}