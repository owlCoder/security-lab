using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

[TestFixture]
public abstract class IntegrationTestBase
{
    protected static CustomWebApplicationFactory<Program> _factory = null!;
    protected static HttpClient _client = null!;

    [OneTimeSetUp]
    public void BaseClassSetup()
    {
        _factory = new CustomWebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void BaseClassTeardown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [SetUp]
    public async Task BaseSetUp()
    {
        // Respawn sam automatski čisti sve tabele, bez obzira ko ih je i kada dodao
        await TestDatabaseFixture.ResetDatabaseAsync();
    }

    // Helper metoda za insertovanje u bazu
    protected async Task InsertAsync<TEntity>(TEntity entity) where TEntity : class
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        db.Set<TEntity>().Add(entity);
        await db.SaveChangesAsync();
    }
}