
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisiteRepositoryRegistrationTests
{
    [Test]
    public void Production_WithoutDatabase_UsesUnavailableRepository()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Authoring"] = null
                        });
                });
            });

        var repository = factory.Services
            .GetRequiredService<IPrerequisiteRepository>();

        Assert.That(
            repository,
            Is.InstanceOf<UnavailablePrerequisiteRepository>());
    }


    [Test]
    public void Development_WithoutDatabase_UsesInMemoryRepository()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Authoring"] = null
                        });
                });
            });

        var repository = factory.Services
            .GetRequiredService<IPrerequisiteRepository>();

        Assert.That(
            repository,
            Is.InstanceOf<InMemoryPrerequisiteRepository>());
    }


    [Test]
    public void Production_WithDatabase_UsesPostgresAdapter()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");


                builder.UseSetting(
                    "ConnectionStrings:Authoring",
                    "Host=localhost;Port=5432;Database=test;Username=test;Password=test");

            });

        using var scope = factory.Services.CreateScope();

        var repository = scope.ServiceProvider
            .GetRequiredService<IPrerequisiteRepository>();

        Assert.That(
            repository,
            Is.InstanceOf<LabPrerequisiteStoreAdapter>());
    }

}
