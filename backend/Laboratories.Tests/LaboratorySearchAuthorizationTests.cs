using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LabVisibility.Contracts;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratorySearchAuthorizationTests
{
    private static readonly Caller Teacher = new();

    [Test]
    public async Task Search_WhenSearchIsDeniedDespiteReadPermission_DoesNotReadStores()
    {
        var repository = new SearchRepository();
        var metadata = new MetadataStore();
        var access = new RevocableSearchAccess { SearchAllowed = false };
        var service = CreateService(repository, metadata, access);

        LabSearchResult result = await service.SearchAsync("lab", Teacher, CancellationToken.None,
            new(LaboratoryDifficulty.Easy));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Forbidden));
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Truncated, Is.False);
            Assert.That(repository.ListCalls, Is.Zero);
            Assert.That(metadata.ReadCalls, Is.Zero);
        });
    }

    [Test]
    public async Task FilterOptions_WhenSearchIsDeniedDespiteReadPermission_DoesNotReadStores()
    {
        var repository = new SearchRepository();
        var metadata = new MetadataStore();
        var access = new RevocableSearchAccess { SearchAllowed = false };
        var service = CreateService(repository, metadata, access);

        LabSearchFilterOptions result = await service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Forbidden));
            Assert.That(result.Difficulties, Is.Empty);
            Assert.That(result.Types, Is.Empty);
            Assert.That(result.Tags, Is.Empty);
            Assert.That(result.MetadataAvailable, Is.False);
            Assert.That(repository.ListCalls, Is.Zero);
            Assert.That(metadata.ReadCalls, Is.Zero);
        });
    }

    [Test]
    public async Task Search_WhenSearchPermissionIsRevoked_NextCallDoesNotReturnPreviousResults()
    {
        var repository = new SearchRepository();
        var metadata = new MetadataStore();
        var access = new RevocableSearchAccess();
        var service = CreateService(repository, metadata, access);

        LabSearchResult before = await service.SearchAsync("lab", Teacher, CancellationToken.None,
            new(LaboratoryDifficulty.Easy));
        access.SearchAllowed = false;
        LabSearchResult after = await service.SearchAsync("lab", Teacher, CancellationToken.None,
            new(LaboratoryDifficulty.Easy));

        Assert.Multiple(() =>
        {
            Assert.That(before.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(before.Items.Select(lab => lab.Id), Is.EqualTo(new[] { repository.Laboratory.Id }));
            Assert.That(after.Status, Is.EqualTo(LabSearchStatus.Forbidden));
            Assert.That(after.Items, Is.Empty);
            Assert.That(after.Truncated, Is.False);
            Assert.That(repository.ListCalls, Is.EqualTo(1));
            Assert.That(metadata.ReadCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FilterOptions_WhenSearchPermissionIsRevoked_NextCallDoesNotReturnPreviousOptions()
    {
        var repository = new SearchRepository();
        var metadata = new MetadataStore();
        var access = new RevocableSearchAccess();
        var service = CreateService(repository, metadata, access);

        LabSearchFilterOptions before = await service.GetFilterOptionsAsync(Teacher, CancellationToken.None);
        access.SearchAllowed = false;
        LabSearchFilterOptions after = await service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(before.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(before.Tags, Is.EqualTo(new[] { MetadataStore.Tag }));
            Assert.That(after.Status, Is.EqualTo(LabSearchStatus.Forbidden));
            Assert.That(after.Difficulties, Is.Empty);
            Assert.That(after.Types, Is.Empty);
            Assert.That(after.Tags, Is.Empty);
            Assert.That(after.MetadataAvailable, Is.False);
            Assert.That(repository.ListCalls, Is.EqualTo(1));
            Assert.That(metadata.ReadCalls, Is.EqualTo(1));
        });
    }

    [TestCase("/api/laboratories/search?q=lab", DenyReason.NotPermitted, HttpStatusCode.Forbidden)]
    [TestCase("/api/labs/search?q=lab", DenyReason.NotPermitted, HttpStatusCode.Forbidden)]
    [TestCase("/api/laboratories/search/filters", DenyReason.NotPermitted, HttpStatusCode.Forbidden)]
    [TestCase("/api/labs/search/filters", DenyReason.NotPermitted, HttpStatusCode.Forbidden)]
    [TestCase("/api/laboratories/search?q=lab", DenyReason.Unauthenticated, HttpStatusCode.Unauthorized)]
    [TestCase("/api/labs/search?q=lab", DenyReason.Unauthenticated, HttpStatusCode.Unauthorized)]
    [TestCase("/api/laboratories/search/filters", DenyReason.Unauthenticated, HttpStatusCode.Unauthorized)]
    [TestCase("/api/labs/search/filters", DenyReason.Unauthenticated, HttpStatusCode.Unauthorized)]
    public async Task Http_WhenAccessIsLostAfterEndpointCheck_ReturnsDenialWithoutData(
        string path, DenyReason reason, HttpStatusCode expectedStatus)
    {
        using var factory = new SearchHostFactory(reason);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-User", "marko:Teacher");

        using var response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(body, Does.Not.Contain(factory.Repository.Laboratory.Title));
            Assert.That(body, Does.Not.Contain(factory.Repository.Laboratory.Id.ToString()));
            Assert.That(body, Does.Not.Contain(MetadataStore.Tag));
            Assert.That(factory.Access.SearchChecks, Is.EqualTo(2));
            Assert.That(factory.Repository.ListCalls, Is.Zero);
            Assert.That(factory.Metadata.ReadCalls, Is.Zero);
        });
    }

    private static LaboratorySearchService CreateService(
        SearchRepository repository, MetadataStore metadata, IAccessDecision access) =>
        new(repository, LabSearchTestContext.CreateMembership(), access, NullLogger<LaboratorySearchService>.Instance, metadata);

    private sealed class Caller : IIdentityContext
    {
        public bool IsAuthenticated => true;
        public string UserId => "marko";
        public PlatformRole? Role => PlatformRole.Teacher;
    }

    private sealed class RevocableSearchAccess : IAccessDecision
    {
        public bool SearchAllowed { get; set; } = true;

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null) =>
            action == LabActions.Search && !SearchAllowed
                ? AccessResult.Deny(DenyReason.NotPermitted)
                : AccessResult.Allow();
    }

    private sealed class DenyAfterEndpointCheck(DenyReason reason) : IAccessDecision
    {
        public int SearchChecks { get; private set; }

        public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null) =>
            action == LabActions.Search && ++SearchChecks > 1
                ? AccessResult.Deny(reason)
                : AccessResult.Allow();
    }

    private sealed class SearchRepository : ILaboratoryRepository
    {
        public Laboratory Laboratory { get; } = new(LabSearchTestContext.CourseId, "Lab restricted result", CallerId.From(Teacher)!.Value);
        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<Laboratory>>([Laboratory]);
        }

        public Task AddAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Laboratory laboratory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MetadataStore : ILaboratoryMetadataStore
    {
        public const string Tag = "restricted-tag";
        public int ReadCalls { get; private set; }

        public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default)
        {
            ReadCalls++;
            return Task.FromResult<LaboratoryMetadata?>(new(laboratoryId, "Existing metadata",
                LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, [Tag]));
        }

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class SearchHostFactory(DenyReason reason) : WebApplicationFactory<Program>
    {
        public SearchRepository Repository { get; } = new();
        public MetadataStore Metadata { get; } = new();
        public DenyAfterEndpointCheck Access { get; } = new(reason);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessDecision>();
                services.RemoveAll<ILaboratoryRepository>();
                services.RemoveAll<ILaboratoryMetadataStore>();
                services.AddSingleton<IAccessDecision>(Access);
                services.AddSingleton<ILaboratoryRepository>(Repository);
                services.AddSingleton<ILaboratoryMetadataStore>(Metadata);
                services.RemoveAll<ICourseMembership>();
                services.AddSingleton<ICourseMembership>(LabSearchTestContext.CreateMembership());
            });
        }
    }
}
