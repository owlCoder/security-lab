using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryFilterHttpTests
{
    private const string Teacher = "marko:Teacher";

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task Search_CombinesAndRemovesFiltersAndResetsWithoutTitle(string route)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();
        LaboratoryResponse first = await CreateLabAsync(client, "Lab A");
        LaboratoryResponse second = await CreateLabAsync(client, "Lab B");
        LaboratoryResponse noMetadata = await CreateLabAsync(client, "Lab no metadata");
        factory.Metadata.Items[first.Id] = new(first.Id, "Objective", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["access", "security"]);
        factory.Metadata.Items[second.Id] = new(second.Id, "Objective", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security"]);

        var combined = await SearchAsync(client, route + "/search?difficulty=easy&type=loganalysis&tag=SECURITY&tag=access");
        var removed = await SearchAsync(client, route + "/search?difficulty=Easy&type=LogAnalysis&tag=security");
        var reset = await SearchAsync(client, route + "/search");

        Assert.Multiple(() =>
        {
            Assert.That(combined.Items.Select(l => l.Id), Is.EqualTo(new[] { first.Id }));
            Assert.That(removed.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
            Assert.That(reset.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id, noMetadata.Id }));
        });
    }

    [TestCase("?difficulty=Unknown")]
    [TestCase("?difficulty=0")]
    [TestCase("?difficulty=Easy&difficulty=Hard")]
    [TestCase("?type=999")]
    [TestCase("?type=LogAnalysis&type=AccessControl")]
    [TestCase("?tag=")]
    [TestCase("?tag=bad%0Avalue")]
    [TestCase("?q=a&q=b")]
    [TestCase("?unexpected=value")]
    public async Task Search_InvalidFiltersReturnControlled400(string parameters)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Get("/api/laboratories/search" + parameters));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Code, Is.EqualTo(LabErrorCodes.InvalidQuery));
    }

    [TestCase("")]
    [TestCase("?q=")]
    [TestCase("?q=%20%20%20")]
    public async Task Search_EmptyTitleBrowsesAccessibleCatalog(string parameters)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();
        LaboratoryResponse own = await CreateLabAsync(client, "Own lab");
        await CreateLabAsync(client, "Hidden lab", "jelena:Teacher");

        var result = await SearchAsync(client, "/api/laboratories/search" + parameters);

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { own.Id }));
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task Options_RequireAuthenticationAndDoNotExposeHiddenTags(string route)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();
        LaboratoryResponse own = await CreateLabAsync(client, "Own lab");
        LaboratoryResponse hidden = await CreateLabAsync(client, "Hidden lab", "jelena:Teacher");
        factory.Metadata.Items[own.Id] = new(own.Id, "Objective", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["visible"]);
        factory.Metadata.Items[hidden.Id] = new(hidden.Id, "Objective", LaboratoryDifficulty.Hard, LaboratoryType.AccessControl, ["secret"]);

        using var response = await client.SendAsync(Get(route + "/search/filters"));
        var options = await response.Content.ReadFromJsonAsync<LaboratorySearchFiltersResponse>();
        using var anonymous = await client.SendAsync(Get(route + "/search/filters", null));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(options!.MetadataAvailable, Is.True);
            Assert.That(options.Tags, Is.EqualTo(new[] { "visible" }));
            Assert.That(options.Difficulties, Is.EqualTo(Enum.GetNames<LaboratoryDifficulty>()));
            Assert.That(options.Types, Is.EqualTo(Enum.GetNames<LaboratoryType>()));
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task MissingStore_OptionsExplainAvailabilityAndFilterSearchReturns503()
    {
        using var factory = new FilterHostFactory(withMetadata: false);
        using var client = factory.CreateClient();

        using var optionsResponse = await client.SendAsync(Get("/api/laboratories/search/filters"));
        var options = await optionsResponse.Content.ReadFromJsonAsync<LaboratorySearchFiltersResponse>();
        using var searchResponse = await client.SendAsync(Get("/api/laboratories/search?difficulty=Easy"));

        Assert.Multiple(() =>
        {
            Assert.That(options!.MetadataAvailable, Is.False);
            Assert.That(options.Tags, Is.Empty);
            Assert.That(searchResponse.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(searchResponse.Headers.CacheControl?.NoStore, Is.True);
        });
        Assert.That((await searchResponse.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Code,
            Is.EqualTo(LabErrorCodes.FiltersUnavailable));
    }

    [TestCase("/api/laboratories/search?difficulty=Easy")]
    [TestCase("/api/laboratories/search/filters")]
    public async Task MetadataFailure_ReturnsGenericErrorWithoutPartialResults(string path)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();
        await CreateLabAsync(client, "Own lab");
        factory.Metadata.Fail = true;

        using var response = await client.SendAsync(Get(path));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(body, Does.Not.Contain("secret-password"));
            Assert.That(body, Does.Not.Contain("Own lab"));
            Assert.That(body, Does.Not.Contain("Exception"));
        });
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task RevokedMembership_HidesSearchResultsAndTagsAndDeniesOpening(string route)
    {
        using var factory = new FilterHostFactory();
        using var client = factory.CreateClient();
        LaboratoryResponse lab = await CreateLabAsync(client, "Lab membership restricted");
        factory.Metadata.Items[lab.Id] = new(lab.Id, "Objective", LaboratoryDifficulty.Easy,
            LaboratoryType.LogAnalysis, ["membership-secret"]);
        Assert.That((await SearchAsync(client, route + "/search?q=membership")).Items.Select(item => item.Id),
            Is.EqualTo(new[] { lab.Id }));
        using (var options = await client.SendAsync(Get(route + "/search/filters")))
            Assert.That((await options.Content.ReadFromJsonAsync<LaboratorySearchFiltersResponse>())!.Tags,
                Does.Contain("membership-secret"));
        using (var open = await client.SendAsync(Get(route + "/" + lab.Id)))
            Assert.That(open.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var callerId = CallerId.From(new TestCaller())!.Value;
        factory.Membership.Set(LabSearchTestContext.CourseId, callerId, CourseRole.None);
        foreach (string path in new[] { "/search?q=membership", "/search?difficulty=Easy", "/search", "/search/filters", "/" + lab.Id })
        {
            using var response = await client.SendAsync(Get(route + path));
            string body = await response.Content.ReadAsStringAsync();
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(path.StartsWith("/search") ? HttpStatusCode.OK : HttpStatusCode.NotFound));
                Assert.That(body, Does.Not.Contain(lab.Title));
                Assert.That(body, Does.Not.Contain(lab.Id.ToString()));
                Assert.That(body, Does.Not.Contain("membership-secret"));
            });
        }
    }

    private sealed class TestCaller : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public string UserId => "marko";
        public PlatformRole? Role => PlatformRole.Teacher;
    }

    private static async Task<LaboratorySearchResponse> SearchAsync(HttpClient client, string path)
    {
        using var response = await client.SendAsync(Get(path));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        return (await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!;
    }

    private static async Task<LaboratoryResponse> CreateLabAsync(HttpClient client, string title, string user = Teacher)
    {
        using var request = Get("/api/laboratories/", user);
        request.Method = HttpMethod.Post;
        request.Content = JsonContent.Create(new { title, courseId = LabSearchTestContext.CourseId });
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<LaboratoryResponse>())!;
    }

    private static HttpRequestMessage Get(string path, string? user = Teacher)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null) request.Headers.Add("X-Test-User", user);
        return request;
    }

    private sealed class FilterHostFactory(bool withMetadata = true) : WebApplicationFactory<Program>
    {
        public MetadataStore Metadata { get; } = new();
        public InMemoryCourseMembership Membership { get; } = LabSearchTestContext.CreateMembership();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILaboratoryMetadataStore>();
                services.RemoveAll<ILaboratoryRepository>();
                services.AddSingleton<ILaboratoryRepository, InMemoryLaboratoryRepository>();
                services.RemoveAll<ICourseMembership>();
                services.AddSingleton<ICourseMembership>(Membership);
                if (withMetadata) services.AddSingleton<ILaboratoryMetadataStore>(Metadata);
            });
        }
    }

    private sealed class MetadataStore : ILaboratoryMetadataStore
    {
        public Dictionary<Guid, LaboratoryMetadata> Items { get; } = [];
        public bool Fail { get; set; }

        public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("Metadata store secret-password");
            return Task.FromResult(Items.GetValueOrDefault(laboratoryId));
        }

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) =>
            throw new NotSupportedException("Laboratory search must not save metadata.");
    }
}
