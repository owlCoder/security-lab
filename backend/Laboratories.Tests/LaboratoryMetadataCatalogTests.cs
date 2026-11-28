using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Testing;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryMetadataCatalogTests
{
    [Test]
    public Task PostThenCatalogThenPatchThenCatalog_ReturnsSavedValuesAcrossRequests() =>
        RunFlow("Development", null);

    [Test]
    public async Task WithoutCatalogStore_HostStartsAndCatalogIsNotMapped()
    {
        using var factory = MetadataTestHost.Create(services =>
            services.RemoveAll<ILaboratoryMetadataStore>());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/catalog/{Guid.NewGuid()}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Any(route => route.RoutePattern.RawText?.StartsWith("/api/catalog") == true), Is.False);
    }

    [TestCase("Development")]
    [TestCase("Production")]
    [TestCase("Testing")]
    public async Task PostgreSql_PostCatalogPatchCatalog_UsesOnePersistentStore(string environment)
    {
        var connection = Environment.GetEnvironmentVariable("LAB02_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Ignore("Run scripts/test-metadata-postgres.ps1 to exercise the persistent HTTP flow.");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        // Never create or clean a database outside the explicitly named test namespace.
        if (settings.Database is null || !settings.Database.StartsWith("lab02_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("LAB02_TEST_POSTGRES must name an isolated lab02_test_ database.");
        settings.Database += "_" + environment.ToLowerInvariant();
        await RunFlow(environment, settings.ConnectionString);
    }

    private static async Task RunFlow(string environment, string? connection)
    {
        var content = new InMemoryLabPublicContentReader();
        var membership = new InMemoryCourseMembership();
        membership.Set(MetadataTestData.CourseId, MetadataTestData.TeacherId,
            Platform.LabVisibility.Contracts.CourseRole.Teacher);
        using var factory = MetadataTestHost.Create(services =>
        {
            // Keep real catalog/metadata services, registrations and shared storage.
            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, TeacherSession>();
            services.RemoveAll<IAccountStatusReader>();
            services.AddSingleton<IAccountStatusReader, TeacherAccount>();
            services.RemoveAll<ILabPublicContentReader>();
            services.AddSingleton<ILabPublicContentReader>(content);
            services.RemoveAll<ICourseMembership>();
            services.AddSingleton<ICourseMembership>(membership);
        }, builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Authoring", connection ?? "");
        });
        using var client = factory.CreateCsrfClient();
        client.DefaultRequestHeaders.Add(TestMetadataSubjectProvider.HeaderName, MetadataTestData.TeacherId.ToString());
        client.DefaultRequestHeaders.Add("Cookie", SessionCookie.Name + "=metadata-catalog-test");
        MetadataTestData.ConfigureAccess(
            factory.Services.GetRequiredService<InMemoryMembershipReader>(),
            factory.Services.GetRequiredService<InMemoryResourceDescriptorResolver>());
        using (var scope = factory.Services.CreateScope())
        {
            if (connection is not null)
            {
                var context = scope.ServiceProvider.GetRequiredService<LaboratoryDbContext>();
                var expected = new NpgsqlConnectionStringBuilder(connection);
                var actual = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
                // Validate the resolved connection, not only the environment input:
                // Development .env/user-secrets must never redirect test DDL.
                if (actual.Database != expected.Database || actual.Host != expected.Host ||
                    actual.Port != expected.Port || actual.Username != expected.Username)
                    throw new InvalidOperationException("Resolved storage does not target the isolated LAB-02 test database.");
                await context.Database.EnsureCreatedAsync();
            }
            await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>().AddAsync(
                Laboratory.Rehydrate(MetadataTestData.LaboratoryId, MetadataTestData.CourseId,
                    "Catalog integration", LabStatus.Draft, MetadataTestData.TeacherId, DateTimeOffset.UtcNow));
        }
        content.Save(LabPublicContent.Create(MetadataTestData.LaboratoryId, MetadataTestData.CourseId,
            MetadataTestData.TeacherId, LabVisibilityState.Draft, "Catalog integration", "Public goal", []));

        var path = $"/api/laboratories/{MetadataTestData.LaboratoryId}/metadata";
        var payload = new { objective = "Initial goal", difficulty = 0, type = 0, tags = new[] { "First" } };
        // A valid session alone must not bypass the shared CSRF protection.
        using (var unprotectedClient = factory.CreateClient())
        {
            unprotectedClient.DefaultRequestHeaders.Add(TestMetadataSubjectProvider.HeaderName, MetadataTestData.TeacherId.ToString());
            unprotectedClient.DefaultRequestHeaders.Add("Cookie", SessionCookie.Name + "=metadata-catalog-test");
            using var rejected = await unprotectedClient.PostAsJsonAsync(path, payload);
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            var error = await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(CsrfToken.InvalidCode));
            using var scope = factory.Services.CreateScope();
            Assert.That(await scope.ServiceProvider.GetRequiredService<ILaboratoryMetadataRepository>()
                .GetByLaboratoryIdAsync(MetadataTestData.LaboratoryId), Is.Null);
        }
        var creates = await Task.WhenAll(client.PostAsJsonAsync(path, payload), client.PostAsJsonAsync(path, payload));
        try
        {
            Assert.That(creates.Select(response => response.StatusCode),
                Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        }
        finally { foreach (var response in creates) response.Dispose(); }
        await AssertCatalog("Initial goal", "Easy", "AccessControl", "first");

        using var updated = await client.PatchAsJsonAsync(path,
            new { objective = "Updated goal", difficulty = 2, type = 1, tags = new[] { "Changed" } });
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertCatalog("Updated goal", "Hard", "LogAnalysis", "changed");

        async Task AssertCatalog(string goal, string difficulty, string type, string tag)
        {
            using var response = await client.GetAsync($"/api/catalog/{MetadataTestData.LaboratoryId}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var result = await response.Content.ReadFromJsonAsync<CatalogLaboratoryResponse>();
            Assert.Multiple(() =>
            {
                Assert.That(result!.Id, Is.EqualTo(MetadataTestData.LaboratoryId));
                Assert.That(result.Objective, Is.EqualTo(goal));
                Assert.That(result.Difficulty, Is.EqualTo(difficulty));
                Assert.That(result.Type, Is.EqualTo(type));
                Assert.That(result.Tags, Is.EqualTo(new[] { tag }));
            });
        }
    }

    [TestCase("Production")]
    [TestCase("Testing")]
    public void ConfiguredPersistentStorage_RegistersAdapterAndBothRoutes(string environment)
    {
        // No DB is contacted: verifies the real registration and route generation.
        // Persistent HTTP behavior is tested separately against PostgreSQL.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Authoring",
                "Host=127.0.0.1;Port=1;Database=metadata_registration_only;Username=test;Password=test;Timeout=1");
        });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<ILaboratoryMetadataRepository>(),
            Is.TypeOf<PersistentMetadataRepository>());
        Assert.That(scope.ServiceProvider.GetRequiredService<ILaboratoryMetadataStore>(),
            Is.Not.TypeOf<MetadataCatalogStore>());
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.EndsWith("/metadata") == true).ToList();
        Assert.That(routes, Has.Count.EqualTo(3));
    }

    private sealed class TeacherAccount : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(id == MetadataTestData.TeacherId ? new(id, AccountStatus.Active) : null);
    }

    private sealed class TeacherSession : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
            Task.FromResult<ValidateSession?>(token == "metadata-catalog-test"
                ? new(Guid.NewGuid(), MetadataTestData.TeacherId, AuthenticationLevel.Active) : null);
        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
