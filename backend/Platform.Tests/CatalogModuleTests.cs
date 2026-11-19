using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Platform.Api.Modules.Laboratories;

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
        builder.Services.AddCatalog(builder.Configuration);
        // These tests inspect route registration without resolving database services.
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        await using var app = builder.Build();
        builder.Configuration[key] = configuredAtMapping ? connection : null;

        app.MapCatalog(app.Configuration);

        Assert.That(((IEndpointRouteBuilder)app).DataSources.Count,
            Is.EqualTo(expectedDataSources));
    }
}
