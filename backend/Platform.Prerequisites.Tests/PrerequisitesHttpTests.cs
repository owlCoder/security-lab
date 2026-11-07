
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisitesHttpTests
{
    [Test]
    public async Task AuthorOfLaboratoryA_CannotAddPrerequisiteToLaboratoryB()
    {
        var authorA = Guid.NewGuid();
        var authorB = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Identity:DevSubjectId"] = authorA.ToString(),
                            ["Authoring:AuthorAccountIds:0"] = authorA.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratoryA = new Laboratory("Laboratory A", authorA);
        var laboratoryB = new Laboratory("Laboratory B", authorB);

        await laboratories.AddAsync(laboratoryA);
        await laboratories.AddAsync(laboratoryB);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratoryB.Id}/prerequisites",
            new { RequiredLaboratoryId = laboratoryA.Id });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }


    [Test]
    public async Task AuthorOfLaboratoryA_CanAddPrerequisiteToOwnLaboratory()
    {
        var authorA = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Identity:DevSubjectId"] = authorA.ToString(),
                            ["Authoring:AuthorAccountIds:0"] = authorA.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var authors = factory.Services
             .GetRequiredService<Platform.Authoring.Application.IAuthorDirectory>();

        Assert.That(authors.IsAuthor(authorA), Is.True,
            "Testni korisnik nije registrovan kao autor.");

        var laboratoryA = new Laboratory("Laboratory A", authorA);
        var requiredLaboratory = new Laboratory(
            "Required Laboratory", authorA);

        await laboratories.AddAsync(laboratoryA);
        await laboratories.AddAsync(requiredLaboratory);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratoryA.Id}/prerequisites",
            new { RequiredLaboratoryId = requiredLaboratory.Id });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task Student_CannotAddPrerequisite()
    {
        var studentId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Identity:DevSubjectId"] = studentId.ToString(),
                            ["Authoring:AuthorAccountIds:0"] = authorId.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = new Laboratory("Laboratory A", authorId);

        await laboratories.AddAsync(laboratory);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites",
            new { RequiredLaboratoryId = Guid.NewGuid() });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AnonymousUser_CannotAddPrerequisite()
    {
        var authorId = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Identity:DevSubjectId"] = "",
                            ["Authoring:AuthorAccountIds:0"] = authorId.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = new Laboratory("Laboratory A", authorId);

        await laboratories.AddAsync(laboratory);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites",
            new { RequiredLaboratoryId = Guid.NewGuid() });

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AuthorOfLaboratoryA_CannotRemovePrerequisiteFromLaboratoryB()
    {
        var authorA = Guid.NewGuid();
        var authorB = Guid.NewGuid();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Identity:DevSubjectId"] = authorA.ToString(),
                        ["Authoring:AuthorAccountIds:0"] = authorA.ToString(),
                        ["Authoring:AuthorAccountIds:1"] = authorB.ToString()
                    });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratoryA = new Laboratory("Laboratory A", authorA);
        var laboratoryB = new Laboratory("Laboratory B", authorB);

        await laboratories.AddAsync(laboratoryA);
        await laboratories.AddAsync(laboratoryB);

        using var client = factory.CreateClient();

        var requiredLaboratoryId = Guid.NewGuid();

        var response = await client.DeleteAsync(
            $"/api/laboratories/{laboratoryB.Id}/prerequisites/{requiredLaboratoryId}");

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
