using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Application.Services;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;
using CentralDecision = Platform.Access.Contracts.IAccessDecision;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryMetadataRequestTests
{
    private const string Path = "/api/laboratories/cccccccc-cccc-cccc-cccc-cccccccccccc/metadata";

    [TestCase("student", HttpStatusCode.Forbidden)]
    [TestCase("other-teacher", HttpStatusCode.Forbidden)]
    [TestCase("anonymous", HttpStatusCode.Unauthorized)]
    public async Task Post_WithoutPermission_DoesNotCreateMetadata(string caller, HttpStatusCode status)
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        if (caller != "anonymous")
            client.DefaultRequestHeaders.Add(TestMetadataSubjectProvider.HeaderName,
                (caller == "student" ? MetadataTestData.StudentId : MetadataTestData.OtherTeacherId).ToString());

        using var response = await client.PostAsJsonAsync(Path, Payload());

        Assert.That(response.StatusCode, Is.EqualTo(status));
        await AssertUnchangedAsync(factory.Services, "POST");
    }

    [Test]
    public async Task Post_MissingLaboratory_DoesNotCreateMetadata()
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();
        // Access permits the course; the LAB repository has no such laboratory.
        await SeedAsync(factory.Services, laboratoryExists: false);
        Authenticate(client);

        using var response = await client.PostAsJsonAsync(Path, Payload());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await AssertUnchangedAsync(factory.Services, "POST");
    }

    [TestCase("POST", "difficulty")]
    [TestCase("POST", "type")]
    [TestCase("PATCH", "difficulty")]
    [TestCase("PATCH", "type")]
    [TestCase("POST", "tag-count")]
    [TestCase("POST", "tag-length")]
    [TestCase("PATCH", "tag-count")]
    [TestCase("PATCH", "tag-length")]
    public async Task InvalidMetadata_IsRejectedWithoutChangingStorage(string method, string invalidField)
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, method: method);
        Authenticate(client);
        var payload = Payload();
        var errorField = invalidField;
        if (invalidField == "tag-count")
        {
            // Raw entries must be limited even when normalization removes duplicates.
            payload["tags"] = Enumerable.Repeat("same", LaboratoryMetadataValidator.MaxTags + 1).ToArray();
            errorField = "tags";
        }
        else if (invalidField == "tag-length")
        {
            payload["tags"] = new[] { new string('x', LaboratoryMetadataValidator.MaxTagLength + 1) };
            errorField = "tags";
        }
        else
            payload[invalidField] = 999;

        using var request = new HttpRequestMessage(new HttpMethod(method), Path) { Content = JsonContent.Create(payload) };
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(body.GetProperty("errors").TryGetProperty(errorField, out _), Is.True);
        await AssertUnchangedAsync(factory.Services, method);
    }

    [TestCase("POST", true)]
    [TestCase("POST", false)]
    [TestCase("PATCH", true)]
    [TestCase("PATCH", false)]
    public async Task OversizedBody_IsRejectedWithAndWithoutContentLength(string method, bool knownLength)
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, method: method);
        Authenticate(client);
        var json = JsonSerializer.Serialize(Payload()).PadRight(LaboratoryMetadataEndpoints.MaxBodyBytes + 1);
        using var request = new HttpRequestMessage(new HttpMethod(method), Path)
        {
            Content = knownLength ? new StringContent(json, Encoding.UTF8, "application/json") : new UnknownLengthContent(json)
        };
        Assert.That(request.Content.Headers.ContentLength.HasValue, Is.EqualTo(knownLength));

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        await AssertUnchangedAsync(factory.Services, method);
    }

    [TestCase("POST", HttpStatusCode.Created)]
    [TestCase("PATCH", HttpStatusCode.OK)]
    public async Task BoundaryValues_AreAccepted_WithOneCentralAccessDecision(string method, HttpStatusCode status)
    {
        using var factory = MetadataTestHost.Create(services =>
        {
            services.RemoveAll<CentralDecision>();
            services.AddSingleton<CountingDecision>();
            services.AddSingleton<CentralDecision>(sp => sp.GetRequiredService<CountingDecision>());
        });
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, method: method);
        Authenticate(client);
        var payload = Payload();
        payload["tags"] = Enumerable.Range(0, LaboratoryMetadataValidator.MaxTags)
            .Select(i => i.ToString("D2") + new string('x', LaboratoryMetadataValidator.MaxTagLength - 2)).ToArray();
        var json = JsonSerializer.Serialize(payload).PadRight(LaboratoryMetadataEndpoints.MaxBodyBytes);
        using var request = new HttpRequestMessage(new HttpMethod(method), Path)
        {
            Content = new UnknownLengthContent(json)
        };

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(status));
        Assert.That(factory.Services.GetRequiredService<CountingDecision>().Calls, Is.EqualTo(1));
        var stored = await factory.Services.GetRequiredService<ILaboratoryMetadataRepository>()
            .GetByLaboratoryIdAsync(MetadataTestData.LaboratoryId);
        Assert.That(stored!.Tags, Has.Count.EqualTo(LaboratoryMetadataValidator.MaxTags));
    }

    private static Dictionary<string, object> Payload() => new()
    {
        ["objective"] = "Learning access control", ["difficulty"] = 0, ["type"] = 0, ["tags"] = new[] { "security" }
    };

    private static void Authenticate(HttpClient client) => client.DefaultRequestHeaders.Add(
        TestMetadataSubjectProvider.HeaderName, MetadataTestData.TeacherId.ToString());

    private static async Task SeedAsync(IServiceProvider services, bool laboratoryExists = true, string method = "POST")
    {
        MetadataTestData.ConfigureAccess(services.GetRequiredService<InMemoryMembershipReader>(),
            services.GetRequiredService<InMemoryResourceDescriptorResolver>());
        if (laboratoryExists)
            await services.GetRequiredService<ILaboratoryRepository>().AddAsync(Laboratory.Rehydrate(
                MetadataTestData.LaboratoryId, MetadataTestData.CourseId, "Review tests", LabStatus.Draft,
                MetadataTestData.TeacherId, DateTimeOffset.UtcNow));
        if (method == "PATCH")
            await services.GetRequiredService<ILaboratoryMetadataRepository>().SaveAsync(new LaboratoryMetadata(
                MetadataTestData.LaboratoryId, "Original objective", LaboratoryDifficulty.Easy,
                LaboratoryType.AccessControl, new[] { "original" }));
    }

    private static async Task AssertUnchangedAsync(IServiceProvider services, string method)
    {
        var stored = await services.GetRequiredService<ILaboratoryMetadataRepository>()
            .GetByLaboratoryIdAsync(MetadataTestData.LaboratoryId);
        if (method == "POST")
            Assert.That(stored, Is.Null);
        else
        {
            Assert.That(stored!.Objective, Is.EqualTo("Original objective"));
            Assert.That(stored.Tags, Is.EqualTo(new[] { "original" }));
        }
    }

    private sealed class CountingDecision(IServiceProvider services) : CentralDecision
    {
        private readonly CentralDecision _inner = ActivatorUtilities.CreateInstance<DefaultDenyAccessDecision>(services);
        public int Calls { get; private set; }
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _inner.DecideAsync(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        public UnknownLengthContent(string json)
        {
            _bytes = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(_bytes));
    }
}
