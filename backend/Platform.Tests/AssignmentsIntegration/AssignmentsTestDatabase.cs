using Npgsql;
using Platform.Api.Modules.Assignments.Persistence;
using Platform.Audit.Composition;
using Testcontainers.PostgreSql;

namespace Platform.Tests.AssignmentsIntegration;

// ASN-04 / ASN-06 TIM 15: izolovana PostgreSQL baza za integracione testove stvarnih adaptera.
//
// Izvor servera, redom:
//   1. AUDIT_TEST_ADMIN_CONNECTION_STRING (isti server koji CI podiže u scripts/audit/test-postgres.ps1);
//   2. Docker (Testcontainers, postgres:17-alpine), isto kao Platform.Laboratories.IntegrationTests.
// Na tom serveru se za svaki fixture pravi NOVA baza sa slučajnim imenom, primene se migracije
// Assignments i Audit šeme (ista baza, da bi audit u transakciji mogao da radi), a na kraju se baza briše.
// Ako nema ni promenljive ni Docker-a, testovi se označe kao Ignored uz jasnu poruku (nikad tiho „zeleno”);
// CI posebno proverava da su ovi fixture-i zaista prošli.
internal sealed class AssignmentsTestDatabase : IAsyncDisposable
{
    public const string AdminConnectionVariable = "AUDIT_TEST_ADMIN_CONNECTION_STRING";

    private readonly PostgreSqlContainer? _container;
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private AssignmentsTestDatabase(
        PostgreSqlContainer? container, string adminConnectionString, string databaseName, string connectionString)
    {
        _container = container;
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<AssignmentsTestDatabase> CreateAsync()
    {
        PostgreSqlContainer? container = null;
        string admin;

        var configured = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            admin = configured;
        }
        else
        {
            container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("assignments_tests")
                .WithUsername("assignments_tests")
                .WithPassword(Guid.NewGuid().ToString("N"))
                .Build();

            var started = false;
            string? reason = null;
            try
            {
                await container.StartAsync();
                started = true;
            }
            catch (Exception exception)
            {
                reason = exception.GetType().Name;
            }

            if (!started)
            {
                await container.DisposeAsync();
                Assert.Ignore(
                    $"PostgreSQL integration tests need {AdminConnectionVariable} or a running Docker engine ({reason}).");
            }

            admin = container.GetConnectionString();
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Pooling = false };
        var databaseName = "assignments_test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = databaseName }.ConnectionString;
        var database = new AssignmentsTestDatabase(container, adminBuilder.ConnectionString, databaseName, connectionString);

        try
        {
            await AssignmentsDatabase.MigrateAsync(connectionString);
            await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(connectionString);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return database;
    }

    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        try
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
        finally
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }
}
