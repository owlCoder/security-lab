using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Entities;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Audit;

[NonParallelizable]
public sealed class AuditInfrastructureIntegrationTests : IntegrationTestBase
{
    private string runtimeConnection = null!;

    [OneTimeSetUp]
    public async Task PrepareAuditRuntime() =>
        runtimeConnection = await AuditTestData.CreateRestrictedRuntimeAsync();

    [Test]
    public async Task AuditReads_LocalEnvOverridesConnection_ShouldUseSharedTestDatabase()
    {
        // Arrange
        const string unrelatedDatabase = "Host=192.0.2.1;Database=not_the_test_database;Username=unused;Timeout=1";
        var user = await UserTestData.AddAsync(Factory, $"audit-config-{Guid.NewGuid():N}@example.com", "Password123!");
        var correlation = Guid.NewGuid().ToString("N");
        Assert.That((await WriteLoginAsync(user.Id, correlation)).IsRecorded, Is.True);
        var contentRoot = Factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var temporaryRoot = Directory.CreateTempSubdirectory("identity-audit-config-").FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        var previousAuditVariable = Environment.GetEnvironmentVariable("ConnectionStrings__Audit");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "docker-compose.yml"), "");
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, ".env"),
                $"ConnectionStrings__Audit={unrelatedDatabase}");
            Environment.SetEnvironmentVariable("ConnectionStrings__Audit", null);
            Directory.SetCurrentDirectory(temporaryRoot);

            // Act
            // The synthetic .env is loaded by Program before AddAudit captures its connection string.
            await using var reader = Factory.WithWebHostBuilder(builder => builder.UseContentRoot(contentRoot));
            using var scope = reader.Services.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

            // Assert
            Assert.That(configuration.GetConnectionString("Audit"), Is.EqualTo(unrelatedDatabase),
                "The control must exercise Program's actual Development .env loading path.");
            // Check before querying: a regression must fail without connecting to an unrelated database.
            Assert.That(db.Database.GetConnectionString(), Is.EqualTo(TestDatabaseFixture.ConnectionString));
            Assert.That(await AuditTestData.GetByCorrelationAsync(reader, correlation), Has.Count.EqualTo(1));
            Assert.That(await AuditTestData.GetByActorAsync(reader, user.Id), Has.Count.EqualTo(1));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("ConnectionStrings__Audit", previousAuditVariable);
            File.Delete(Path.Combine(temporaryRoot, ".env"));
            File.Delete(Path.Combine(temporaryRoot, "docker-compose.yml"));
            Directory.Delete(temporaryRoot);
        }
    }

    [Test]
    public async Task CreateRestrictedRuntime_ExistingRole_ShouldKeepEarlierConnectionUsable()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, $"audit-role-{Guid.NewGuid():N}@example.com", "Password123!");
        var correlation = Guid.NewGuid().ToString("N");

        // Act
        await AuditTestData.CreateRestrictedRuntimeAsync();
        // WriteLoginAsync deliberately keeps the connection obtained before the repeated setup.
        var result = await WriteLoginAsync(user.Id, correlation);

        // Assert
        Assert.That(result.IsRecorded, Is.True);
        Assert.That(await AuditTestData.GetByCorrelationAsync(Factory, correlation), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ResetDatabase_ExistingAuditHistory_ShouldClearIdentityAndPreserveAudit()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, $"audit-reset-{Guid.NewGuid():N}@example.com", "Password123!");
        var correlation = Guid.NewGuid().ToString("N");
        Assert.That((await WriteLoginAsync(user.Id, correlation)).IsRecorded, Is.True);

        // Act
        await TestDatabaseFixture.ResetDatabaseAsync();

        // Assert
        Assert.That(await GetAsync<UserAccount>(user.Id), Is.Null);
        Assert.That(await AuditTestData.GetByCorrelationAsync(Factory, correlation), Has.Count.EqualTo(1));
    }

    private async Task<AuditWriteResult> WriteLoginAsync(Guid userId, string correlation)
    {
        await using var host = CreateAuditedHost();
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityAuditProducer>()
            .LoginSucceededAsync(userId, correlation);
    }

    private WebApplicationFactory<Program> CreateAuditedHost() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuditDbContext>();
            services.RemoveAll<DbContextOptions<AuditDbContext>>();
            services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(runtimeConnection,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        }));
}
