using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using AccessActionScope = Platform.Access.Domain.Enums.ActionScope;
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
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratorySearchCatalogContractTests
{
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid Course = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Test]
    public void Catalog_RoutesDeclareExactlyOneRegisteredActionAndPolicyWithoutLegacyMetadata()
    {
        using var factory = new CatalogContractFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var actions = scope.ServiceProvider.GetRequiredService<IActionCatalog>();
        var policies = scope.ServiceProvider.GetRequiredService<IAccessPolicyRegistry>();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/catalog/", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.That(endpoints.Select(endpoint => endpoint.RoutePattern.RawText),
            Is.EquivalentTo(new[] { "/api/catalog/", "/api/catalog/{id:guid}" }));
        Assert.That(actions.TryGet("catalog.read", out var definition), Is.True);
        Assert.That(policies.TryGet("catalog.read", out var policy), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(definition!.Scope, Is.EqualTo(AccessActionScope.Authenticated));
            Assert.That(definition.ResourceType, Is.Null);
            Assert.That(policy!.Id, Is.EqualTo("catalog.read"));
        });

        foreach (var endpoint in endpoints)
        {
            var requirements = endpoint.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>();
            Assert.That(requirements, Has.Count.EqualTo(1), endpoint.DisplayName);
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                    Is.EqualTo(new[] { "GET" }), endpoint.DisplayName);
                Assert.That(requirements[0].Action, Is.EqualTo("catalog.read"), endpoint.DisplayName);
                Assert.That(requirements[0].ResourceType, Is.Null, endpoint.DisplayName);
                Assert.That(requirements[0].IdRouteParam, Is.Null, endpoint.DisplayName);
#pragma warning disable CS0618
                Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null, endpoint.DisplayName);
                Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null, endpoint.DisplayName);
#pragma warning restore CS0618
            });
        }
    }

    [Test]
    public void ProductionConfiguration_ResolvesSharedPostgresMetadataStore()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Authoring",
                "Host=127.0.0.1;Port=1;Database=lab05_composition;Username=unused;Password=unused;Timeout=1");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });
        using var scope = factory.Services.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<ILaboratoryMetadataStore>();

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.InstanceOf<PostgresLaboratoryStore>());
            Assert.That(metadata, Is.SameAs(scope.ServiceProvider.GetRequiredService<ILaboratoryStore>()));
            Assert.That(scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>(),
                Is.InstanceOf<PostgresLaboratoryRepository>());
            Assert.That(scope.ServiceProvider.GetRequiredService<LaboratorySearchService>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<CatalogService>(), Is.Not.Null);
        });
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task SearchAndCatalog_UseSameMetadataFilters_AndSearchResultCanBeOpened(string route)
    {
        using var factory = new CatalogContractFactory();
        using var client = factory.CreateClient();
        Guid matching = await factory.SeedAsync("Access control", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security", "access"]);
        await factory.SeedAsync("Different difficulty", LaboratoryDifficulty.Hard,
            LaboratoryType.LogAnalysis, ["security"]);
        await factory.SeedAsync("Different type", LaboratoryDifficulty.Easy,
            LaboratoryType.AccessControl, ["security"]);
        await factory.SeedAsync("Different tag", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["other"]);
        const string filters = "?difficulty=easy&type=loganalysis&tag=SECURITY";

        using var searchResponse = await client.SendAsync(Request(route + "/search" + filters));
        using var catalogResponse = await client.SendAsync(Request("/api/catalog/" + filters));
        Assert.Multiple(() =>
        {
            Assert.That(searchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(catalogResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(searchResponse.Headers.CacheControl?.NoStore, Is.True);
        });
        var search = (await searchResponse.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!;
        var catalog = (await catalogResponse.Content.ReadFromJsonAsync<CatalogLaboratoryResponse[]>())!;
        Assert.Multiple(() =>
        {
            Assert.That(search.Items.Select(item => item.Id), Is.EqualTo(new[] { matching }));
            Assert.That(search.Items.Select(item => item.Id),
                Is.EquivalentTo(catalog.Select(item => item.Id)));
            Assert.That(catalog.Single().Difficulty, Is.EqualTo(nameof(LaboratoryDifficulty.Easy)));
            Assert.That(catalog.Single().Type, Is.EqualTo(nameof(LaboratoryType.LogAnalysis)));
        });

        using var optionsResponse = await client.SendAsync(Request(route + "/search/filters"));
        Assert.That(optionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var options = (await optionsResponse.Content.ReadFromJsonAsync<LaboratorySearchFiltersResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(options.MetadataAvailable, Is.True);
            Assert.That(options.Tags, Does.Contain("security"));
        });
        using var openResponse = await client.SendAsync(Request(route + "/" + search.Items.Single().Id));
        Assert.That(openResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await openResponse.Content.ReadFromJsonAsync<LaboratoryResponse>())!.Id,
            Is.EqualTo(matching));
    }

    [TestCase(null)]
    [TestCase("invalid-session")]
    public async Task Catalog_RejectsDevelopmentHeaderWithoutValidSession(string? sessionToken)
    {
        using var factory = new CatalogContractFactory();
        using var client = factory.CreateClient();
        Guid labId = await factory.SeedAsync("Session protected", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security"]);

        foreach (string path in new[] { "/api/catalog/", "/api/catalog/" + labId })
        {
            using var request = CatalogRequest(path, sessionToken);
            request.Headers.Add("X-Test-User", Teacher + ":Teacher");
            using var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    [TestCase(null)]
    public async Task Catalog_CurrentAccountStatusRejectsPreviouslyValidatedSession(AccountStatus? accountStatus)
    {
        using var factory = new CatalogContractFactory(accountStatus: accountStatus);
        using var client = factory.CreateClient();
        Guid labId = await factory.SeedAsync("Account protected", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security"]);

        foreach (string path in new[] { "/api/catalog/", "/api/catalog/" + labId })
        {
            using var request = CatalogRequest(path);
            using var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [TestCase(AuthenticationLevel.None, false)]
    [TestCase(AuthenticationLevel.MfaPending, false)]
    [TestCase(AuthenticationLevel.Active, true)]
    public async Task Catalog_RejectsSessionWithoutFullyAuthenticatedSubject(
        AuthenticationLevel authenticationLevel, bool emptySessionUserId)
    {
        using var factory = new CatalogContractFactory(
            authenticationLevel: authenticationLevel, emptySessionUserId: emptySessionUserId);
        using var client = factory.CreateClient();
        Guid labId = await factory.SeedAsync("Authentication protected", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security"]);

        foreach (string path in new[] { "/api/catalog/", "/api/catalog/" + labId })
        {
            using var request = CatalogRequest(path);
            request.Headers.Add("X-Test-User", Teacher + ":Teacher");
            using var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [Test]
    public async Task Catalog_ValidSessionPreservesVisibilityAndHiddenDetail404()
    {
        using var factory = new CatalogContractFactory();
        using var client = factory.CreateClient();
        Guid visible = await factory.SeedAsync("Visible draft", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security"]);
        Guid hidden = await factory.SeedAsync("Other course", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["security"], Guid.NewGuid());

        using var listRequest = CatalogRequest("/api/catalog/");
        using var listResponse = await client.SendAsync(listRequest);
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var catalog = (await listResponse.Content.ReadFromJsonAsync<CatalogLaboratoryResponse[]>())!;
        Assert.That(catalog.Select(item => item.Id), Is.EqualTo(new[] { visible }));

        using var detailRequest = CatalogRequest("/api/catalog/" + visible);
        using var detailResponse = await client.SendAsync(detailRequest);
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await detailResponse.Content.ReadFromJsonAsync<CatalogLaboratoryResponse>())!.Id,
            Is.EqualTo(visible));

        foreach (Guid id in new[] { hidden, Guid.NewGuid() })
        {
            using var request = CatalogRequest("/api/catalog/" + id);
            using var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    private static HttpRequestMessage CatalogRequest(string path, string? sessionToken = Teacher)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (sessionToken is not null)
            request.Headers.Add("Cookie", SessionCookie.Name + "=" + sessionToken);
        return request;
    }

    private static HttpRequestMessage Request(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        // LAB-05 currently uses the Development LAB identity; LAB-02 uses a validated session.
        request.Headers.Add("X-Test-User", Teacher + ":Teacher");
        request.Headers.Add("Cookie", SessionCookie.Name + "=" + Teacher);
        return request;
    }

    private sealed class CatalogContractFactory(
        AccountStatus? accountStatus = AccountStatus.Active,
        AuthenticationLevel authenticationLevel = AuthenticationLevel.Active,
        bool emptySessionUserId = false) : WebApplicationFactory<Program>
    {
        private readonly InMemoryLaboratoryRepository repository = new();
        private readonly MetadataStore metadata = new();
        private readonly InMemoryLabPublicContentReader content = new();

        public async Task<Guid> SeedAsync(string title, LaboratoryDifficulty difficulty,
            LaboratoryType type, string[] tags, Guid? courseId = null)
        {
            var lab = new Laboratory(courseId ?? Course, title, Guid.Parse(Teacher));
            await repository.AddAsync(lab);
            await metadata.SaveAsync(new(lab.Id, "Shared LAB-02 objective", difficulty, type, tags));
            content.Save(LabPublicContent.Create(lab.Id, lab.CourseId, Guid.Parse(Teacher),
                LabVisibilityState.Draft, title, "Shared LAB-02 objective", []));
            return lab.Id;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                // Real endpoint handlers/services share only replaced infrastructure sources.
                services.AddScoped<CatalogService>();
                services.RemoveAll<ILaboratoryRepository>();
                services.AddSingleton<ILaboratoryRepository>(repository);
                services.RemoveAll<ILaboratoryMetadataStore>();
                services.AddSingleton<ILaboratoryMetadataStore>(metadata);
                services.RemoveAll<ILabPublicContentReader>();
                services.AddSingleton<ILabPublicContentReader>(content);
                services.RemoveAll<ICourseMembership>();
                var membership = new InMemoryCourseMembership();
                membership.Set(Course, Guid.Parse(Teacher), CourseRole.Teacher);
                services.AddSingleton<ICourseMembership>(membership);
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(new TestSessionManager(authenticationLevel, emptySessionUserId));
                services.RemoveAll<IAccountStatusReader>();
                services.AddSingleton<IAccountStatusReader>(new TestAccounts(accountStatus));
            });
        }
    }

    private sealed class MetadataStore : ILaboratoryMetadataStore
    {
        private readonly Dictionary<Guid, LaboratoryMetadata> items = [];
        public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default) =>
            Task.FromResult(items.GetValueOrDefault(laboratoryId));
        public Task SaveAsync(LaboratoryMetadata value, CancellationToken ct = default)
        {
            items[value.LaboratoryId] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class TestAccounts(AccountStatus? status) : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(accountId == Guid.Parse(Teacher) && status is { } currentStatus
                ? new(accountId, currentStatus) : null);
    }

    private sealed class TestSessionManager(AuthenticationLevel level, bool emptyUserId) : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
            Task.FromResult<ValidateSession?>(token == Teacher
                ? new(Guid.NewGuid(), emptyUserId ? Guid.Empty : Guid.Parse(Teacher), level) : null);
        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
