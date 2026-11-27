using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Api.Development;
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratorySearchDemoTests
{
    private const string Teacher = "11111111-1111-1111-1111-111111111111:Teacher";
    private static readonly Guid SearchDemoId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [TestCase("/api/laboratories")]
    [TestCase("/api/labs")]
    public async Task EnabledDemo_UsesExistingMembership_AndEveryTeacherResultCanBeOpened(string route)
    {
        using var factory = new DemoHostFactory(enabled: true);
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Request(route + "/search", Teacher));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = (await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(item => item.Id),
                Is.EquivalentTo(new[] { LabVisibilityDemoSeed.DraftLabId, SearchDemoId }));
            Assert.That(result.Items.All(item => item.Status == nameof(LabStatus.Draft)), Is.True);
            Assert.That(result.Truncated, Is.False);
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        });

        foreach (LaboratorySearchItem item in result.Items)
        {
            using var opened = await client.SendAsync(Request(route + "/" + item.Id, Teacher));
            Assert.That(opened.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var lab = (await opened.Content.ReadFromJsonAsync<LaboratoryResponse>())!;
            Assert.Multiple(() =>
            {
                Assert.That(lab.Id, Is.EqualTo(item.Id));
                Assert.That(lab.CourseId, Is.EqualTo(LabVisibilityDemoSeed.DemoCourseId));
                Assert.That(lab.CreatedBy, Is.EqualTo(LabVisibilityDemoSeed.TeacherId));
            });
        }

        using var partial = await client.SendAsync(Request(route + "/search?q=integriteta", Teacher));
        Assert.That(partial.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await partial.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!.Items
            .Select(item => item.Id), Is.EqualTo(new[] { LabVisibilityDemoSeed.DraftLabId }));
    }

    [TestCase(null, HttpStatusCode.Unauthorized)]
    [TestCase("22222222-2222-2222-2222-222222222222:Student", HttpStatusCode.OK)]
    [TestCase("33333333-3333-3333-3333-333333333333:Teacher", HttpStatusCode.OK)]
    [TestCase("marko:Teacher", HttpStatusCode.OK)]
    public async Task EnabledDemo_DoesNotExposeDraftsToOtherCallers(string? user, HttpStatusCode expected)
    {
        using var factory = new DemoHostFactory(enabled: true);
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Request("/api/laboratories/search", user));

        Assert.That(response.StatusCode, Is.EqualTo(expected));
        if (expected == HttpStatusCode.OK)
            Assert.That((await response.Content.ReadFromJsonAsync<LaboratorySearchResponse>())!.Items, Is.Empty);
    }

    [TestCase(null)]
    [TestCase(false)]
    public async Task DefaultOrDisabledTestHost_DoesNotSeedDemoData(bool? enabled)
    {
        using var factory = new DemoHostFactory(enabled: enabled);
        var repository = factory.Services.GetRequiredService<ILaboratoryRepository>();

        Assert.That(await repository.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task RepeatedSeed_PreservesExistingTitle_AndDoesNotCopyPublishedLabAsDraft()
    {
        using var factory = new DemoHostFactory(enabled: true);
        var repository = factory.Services.GetRequiredService<ILaboratoryRepository>();
        Laboratory existing = (await repository.GetByIdAsync(LabVisibilityDemoSeed.DraftLabId))!;
        var source = LabVisibilityDemoSeed.Create().Single(item => item.Content.LabId == existing.Id).Content;
        Assert.That(existing.Title, Is.EqualTo(source.Title));
        existing.Rename("Locally renamed demonstration laboratory");
        await repository.UpdateAsync(existing);

        await factory.Services.SeedLaboratorySearchDemoAsync();

        IReadOnlyList<Laboratory> seeded = await repository.ListAsync();
        Laboratory? renamed = await repository.GetByIdAsync(existing.Id);
        Laboratory? published = await repository.GetByIdAsync(LabVisibilityDemoSeed.PublishedLabId);
        Assert.Multiple(() =>
        {
            Assert.That(seeded, Has.Count.EqualTo(2));
            Assert.That(renamed!.Title,
                Is.EqualTo("Locally renamed demonstration laboratory"));
            Assert.That(published, Is.Null);
        });
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task OutsideDevelopment_EnabledFlagDoesNotSeedRepository(string environment)
    {
        var repository = new InMemoryLaboratoryRepository();
        using var factory = new DemoHostFactory(environment, enabled: true, repository: repository);
        await factory.Services.SeedLaboratorySearchDemoAsync();

        Assert.That(await repository.ListAsync(), Is.Empty);
    }

    [Test]
    public void NonMemoryRepository_IsNeverAccessedByDemoSeed()
    {
        var repository = new RejectingRepository();
        using var factory = new DemoHostFactory(enabled: true, repository: repository);

        Assert.DoesNotThrow(() => _ = factory.Services);
        Assert.That(repository.Calls, Is.Zero);
    }

    private static HttpRequestMessage Request(string path, string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null) request.Headers.Add("X-Test-User", user);
        return request;
    }

    private sealed class DemoHostFactory(
        string environment = "Development", bool? enabled = null, ILaboratoryRepository? repository = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            if (enabled.HasValue)
                builder.UseSetting(LaboratorySearchDemoSeed.EnabledSetting, enabled.Value.ToString());
            builder.ConfigureLogging(logging => logging.ClearProviders());
            if (repository is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ILaboratoryRepository>();
                    services.AddSingleton(repository);
                });
            }
        }
    }

    private sealed class RejectingRepository : ILaboratoryRepository
    {
        public int Calls { get; private set; }
        private Exception UnexpectedCall() { Calls++; return new InvalidOperationException("Demo seed must not access persistent repositories."); }
        public Task AddAsync(Laboratory laboratory, CancellationToken ct = default) => throw UnexpectedCall();
        public Task<Laboratory?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw UnexpectedCall();
        public Task<IReadOnlyList<Laboratory>> ListAsync(CancellationToken ct = default) => throw UnexpectedCall();
        public Task UpdateAsync(Laboratory laboratory, CancellationToken ct = default) => throw UnexpectedCall();
        public Task<LabRevision> AddRevisionAsync(Guid laboratoryId, CancellationToken ct = default) => throw UnexpectedCall();
        public Task<IReadOnlyList<LabRevision>> GetRevisionsAsync(Guid laboratoryId, CancellationToken ct = default) => throw UnexpectedCall();
    }
}
