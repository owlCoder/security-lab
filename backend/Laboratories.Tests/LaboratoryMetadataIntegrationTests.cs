using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryMetadataIntegrationTests
{
    [TestCase("Production", "POST")]
    [TestCase("Production", "PATCH")]
    [TestCase("Testing", "POST")]
    [TestCase("Testing", "PATCH")]
    public async Task WithoutStorage_MetadataRoutesAreNotExposed(string environment, string method)
    {
        // Real application registrations and DI validation; no replacement services.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient();
        var routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();

        Assert.That(factory.Services.GetService<ILaboratoryMetadataRepository>(), Is.Null);
        Assert.That(routes.Any(route => route.RoutePattern.RawText?.EndsWith("/metadata") == true), Is.False);

        using var request = new HttpRequestMessage(new HttpMethod(method),
            $"/api/laboratories/{Guid.NewGuid()}/metadata")
        {
            Content = JsonContent.Create(new { objective = "Unavailable", difficulty = 0, type = 0, tags = new[] { "test" } })
        };
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ConcurrentPosts_CreateExactlyOneRecord_AndReturnConflictForTheOther()
    {
        var repository = new ConcurrentCreateRepository();
        using var factory = MetadataTestHost.Create(services =>
        {
            services.RemoveAll<ILaboratoryMetadataRepository>();
            services.AddSingleton<ILaboratoryMetadataRepository>(repository);
        });
        using var client = factory.CreateClient();
        MetadataTestData.ConfigureAccess(
            factory.Services.GetRequiredService<InMemoryMembershipReader>(),
            factory.Services.GetRequiredService<InMemoryResourceDescriptorResolver>());
        await factory.Services.GetRequiredService<ILaboratoryRepository>().AddAsync(
            Laboratory.Rehydrate(MetadataTestData.LaboratoryId, MetadataTestData.CourseId,
                "Concurrent metadata", LabStatus.Draft, MetadataTestData.TeacherId, DateTimeOffset.UtcNow));
        client.DefaultRequestHeaders.Add(TestMetadataSubjectProvider.HeaderName, MetadataTestData.TeacherId.ToString());

        Task<HttpResponseMessage> Post(string objective) => client.PostAsJsonAsync(
            $"/api/laboratories/{MetadataTestData.LaboratoryId}/metadata",
            new { objective, difficulty = 0, type = 0, tags = new[] { "test" } });

        var responses = await Task.WhenAll(Post("First request"), Post("Second request"))
            .WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.That(responses.Select(response => response.StatusCode),
                Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            var created = await responses.Single(response => response.StatusCode == HttpStatusCode.Created)
                .Content.ReadFromJsonAsync<MetadataResult>();
            var stored = await repository.Inner.GetByLaboratoryIdAsync(MetadataTestData.LaboratoryId);
            Assert.That(stored!.Objective, Is.EqualTo(created!.Objective), "The losing request must not overwrite the winner.");
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private sealed record MetadataResult(string Objective);

    // Both requests reach storage before either creates a record. Get returns
    // its pre-barrier snapshot so the former check-then-save implementation
    // deterministically produces two 201s and fails this regression test.
    private sealed class ConcurrentCreateRepository : ILaboratoryMetadataRepository
    {
        public InMemoryLaboratoryMetadataRepository Inner { get; } = new();
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        private async Task Rendezvous(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _bothArrived.TrySetResult();
            await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        public async Task<bool> TryAddAsync(LaboratoryMetadata metadata, CancellationToken cancellationToken = default)
        {
            await Rendezvous(cancellationToken);
            return await Inner.TryAddAsync(metadata, cancellationToken);
        }

        public async Task<LaboratoryMetadata?> GetByLaboratoryIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var snapshot = await Inner.GetByLaboratoryIdAsync(id, cancellationToken);
            await Rendezvous(cancellationToken);
            return snapshot;
        }

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken cancellationToken = default) =>
            Inner.SaveAsync(metadata, cancellationToken);
    }
}
