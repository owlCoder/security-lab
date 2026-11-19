
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Prerequisites.Tests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PrerequisitesPostgresHttpTests
{
    [Test]
    public async Task Author_CanAddAndRemovePrerequisite_WithPostgresPersistence()
    {
        await using var database =
            new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("prerequisites_tests")
                .WithUsername("prerequisites_tests")
                .WithPassword(Guid.NewGuid().ToString("N"))
                .Build();

        await database.StartAsync();

        var authorId = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.UseSetting(
                    "ConnectionStrings:Authoring",
                    database.GetConnectionString());    

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Authoring"] =
                                database.GetConnectionString(),

                            ["Identity:DevSubjectId"] =
                                authorId.ToString(),

                            ["Authoring:AuthorAccountIds:0"] =
                                authorId.ToString()
                        });
                });
            });

        var laboratory = new Laboratory(
            Guid.NewGuid(),
            "Laboratory A",
            authorId,
            TimeProvider.System);

        var requiredLaboratory = new Laboratory(
            Guid.NewGuid(),
            "Required Laboratory",
            authorId,
            TimeProvider.System);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<LaboratoryDbContext>();

            await context.Database.MigrateAsync();

            var laboratories = scope.ServiceProvider
                .GetRequiredService<ILaboratoryRepository>();

            await laboratories.AddAsync(laboratory);
            await laboratories.AddAsync(requiredLaboratory);
        }

        using var client = factory.CreateClient();

        var endpoint =
            $"/api/laboratories/{laboratory.Id}/prerequisites";

        // Add prerequisite through HTTP.
        using var addResponse = await client.PostAsJsonAsync(
            endpoint,
            new
            {
                RequiredLaboratoryId = requiredLaboratory.Id
            });

        Assert.That(
            addResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.Created));

        // Verify that PostgreSQL contains the prerequisite.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<LaboratoryDbContext>();

            var exists = await context.LabPrerequisites
                .AsNoTracking()
                .AnyAsync(p =>
                    p.LaboratoryId == laboratory.Id &&
                    p.PrerequisiteLabId == requiredLaboratory.Id);

            Assert.That(exists, Is.True);
        }

        // Remove prerequisite through HTTP.
        using var removeResponse = await client.DeleteAsync(
            $"{endpoint}/{requiredLaboratory.Id}");

        Assert.That(
            removeResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));

        // Verify that the prerequisite was removed from PostgreSQL.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<LaboratoryDbContext>();

            var exists = await context.LabPrerequisites
                .AsNoTracking()
                .AnyAsync(p =>
                    p.LaboratoryId == laboratory.Id &&
                    p.PrerequisiteLabId == requiredLaboratory.Id);

            Assert.That(exists, Is.False);
        }
    }
}
