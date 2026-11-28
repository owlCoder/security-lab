using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Modules.Laboratories;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;

namespace Platform.Tests;

public class CatalogModuleTests
{
    [TestCase(false, true, 0)]
    [TestCase(true, true, 1)]
    [TestCase(true, false, 1)]
    [TestCase(false, false, 0)]
    public async Task Routes_FollowServiceRegistration_WhenConfigurationChanges(
        bool configuredAtRegistration, bool configuredAtMapping, int expectedDataSources)
    {
        var builder = WebApplication.CreateBuilder();
        const string key = "ConnectionStrings:Authoring";
        const string connection = "Host=localhost;Database=catalog_test";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = configuredAtRegistration ? connection : null
        });
        // Catalog now requires a registered metadata store. Keep the original
        // configuration-change assertions, with the minimal storage registration
        // that the real application supplies when Authoring is configured.
        if (configuredAtRegistration)
            builder.Services.AddSingleton<ILaboratoryMetadataStore, RegistrationOnlyMetadataStore>();
        builder.Services.AddCatalog(builder.Configuration);
        // These tests inspect route registration without resolving database services.
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        await using var app = builder.Build();
        builder.Configuration[key] = configuredAtMapping ? connection : null;

        app.MapCatalog(app.Configuration);

        Assert.That(((IEndpointRouteBuilder)app).DataSources.Count,
            Is.EqualTo(expectedDataSources));
    }

    private sealed class RegistrationOnlyMetadataStore : ILaboratoryMetadataStore
    {
        public Task<LaboratoryMetadata?> GetAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException("This test only inspects route registration.");

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) =>
            throw new NotSupportedException("This test only inspects route registration.");
    }
}
