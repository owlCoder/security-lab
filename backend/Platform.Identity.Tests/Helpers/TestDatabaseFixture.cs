using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Identity.Persistence;
using Respawn;
using Testcontainers.PostgreSql;
using Table = Respawn.Graph.Table;

namespace Platform.Identity.Tests.Helpers;

[SetUpFixture]
public class TestDatabaseFixture
{
    private static readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public static string ConnectionString { get; private set; } = string.Empty;
    private static Respawner _respawner = null!;
    private static NpgsqlConnection _connection = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        // 1. Pokretanje docker containera test baze
        await _dbContainer.StartAsync();
        ConnectionString = _dbContainer.GetConnectionString();

        // 2. Kreiranje baze preko migracija
        await ApplyMigrationsAsync();

        // 3. Inicijalizacija Respawn
        _connection = new NpgsqlConnection(ConnectionString);
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = [new Table("__EFMigrationsHistory") ]
        });
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await _connection.DisposeAsync();
        await _dbContainer.StopAsync();
        await _dbContainer.DisposeAsync();
    }

    public static async Task ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(_connection);
    }

    // Definicija metode za migracije
    private static async Task ApplyMigrationsAsync()
    {
        await using var factory = new CustomWebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        
        // Uzima se PlatformIdentityDbContext iz DI kontejnera
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        
        // Primenjivanje migracije na testnu bazu
        await db.Database.MigrateAsync(); 
    }
}