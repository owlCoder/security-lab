using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Platform.Tests;

public sealed class CatalogMetadataContractTests
{
    private static readonly Guid TeacherId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid CourseId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    public async Task Detail_ReturnsDifficultyAndTypeAsEnumStrings()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:Authoring",
                    "Host=localhost;Database=catalog_contract_test;Username=test;Password=test");

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISessionManager>();
                    services.AddSingleton<ISessionManager, TestSessionManager>();

                    services.RemoveAll<IAccountStatusReader>();
                    services.AddSingleton<IAccountStatusReader, ActiveTestAccounts>();

                    services.RemoveAll<ILaboratoryRepository>();
                    services.AddSingleton<InMemoryLaboratoryRepository>();
                    services.AddSingleton<ILaboratoryRepository>(
                        provider => provider.GetRequiredService<InMemoryLaboratoryRepository>());

                    services.RemoveAll<ILaboratoryMetadataStore>();
                    services.AddSingleton<TestMetadataStore>();
                    services.AddSingleton<ILaboratoryMetadataStore>(
                        provider => provider.GetRequiredService<TestMetadataStore>());

                    services.RemoveAll<ILabPublicContentReader>();
                    services.AddSingleton<ILabPublicContentReader>(
                        provider => provider.GetRequiredService<InMemoryLabPublicContentReader>());

                    services.RemoveAll<ICourseMembership>();
                    services.AddSingleton<ICourseMembership>(
                        provider => provider.GetRequiredService<InMemoryCourseMembership>());
                });
            });

        var lab = new Laboratory(CourseId, "Contract test lab", TeacherId);

        await factory.Services
            .GetRequiredService<ILaboratoryRepository>()
            .AddAsync(lab);

        await factory.Services
            .GetRequiredService<ILaboratoryMetadataStore>()
            .SaveAsync(
                new LaboratoryMetadata(
                    lab.Id,
                    "Provera API ugovora",
                    LaboratoryDifficulty.Hard,
                    LaboratoryType.MfaAndSessions,
                    new[] { "contract-test" }));

        factory.Services
            .GetRequiredService<InMemoryLabPublicContentReader>()
            .Save(
                LabPublicContent.Create(
                    lab.Id,
                    CourseId,
                    TeacherId,
                    LabVisibilityState.Draft,
                    lab.Title,
                    "Provera API ugovora",
                    Array.Empty<PublicTask>()));

        factory.Services
            .GetRequiredService<InMemoryCourseMembership>()
            .Set(CourseId, TeacherId, CourseRole.Teacher);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/catalog/{lab.Id:D}");

        request.Headers.Add(
            "Cookie",
            $"{SessionCookie.Name}={TeacherId:D}");

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(
                json.RootElement.GetProperty("difficulty").GetString(),
                Is.EqualTo("Hard"));

            Assert.That(
                json.RootElement.GetProperty("type").GetString(),
                Is.EqualTo("MfaAndSessions"));
        });
    }

    private sealed class TestMetadataStore : ILaboratoryMetadataStore
    {
        private readonly ConcurrentDictionary<Guid, LaboratoryMetadata> metadata =
            new();

        public Task<LaboratoryMetadata?> GetAsync(
            Guid laboratoryId,
            CancellationToken ct = default)
        {
            metadata.TryGetValue(laboratoryId, out var value);
            return Task.FromResult(value);
        }

        public Task SaveAsync(
            LaboratoryMetadata value,
            CancellationToken ct = default)
        {
            metadata[value.LaboratoryId] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSessionManager : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(
            string token,
            CancellationToken ct = default)
        {
            ValidateSession? session =
                token == TeacherId.ToString("D")
                    ? new ValidateSession(
                        Guid.NewGuid(),
                        TeacherId,
                        AuthenticationLevel.Active)
                    : null;

            return Task.FromResult<ValidateSession?>(session);
        }

        public Task<int> RevokeAllForUser(
            Guid userId,
            string reason,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> RevokeSession(
            Guid ownerUserId,
            Guid sessionHandle,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionInfo>> ListForUser(
            Guid userId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ActiveTestAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            AccountSnapshot? account =
                accountId == TeacherId
                    ? new AccountSnapshot(accountId, AccountStatus.Active)
                    : null;

            return Task.FromResult(account);
        }
    }
}
