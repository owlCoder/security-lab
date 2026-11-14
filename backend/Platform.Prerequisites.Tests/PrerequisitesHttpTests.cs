
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

        var laboratoryA = new Laboratory(Guid.NewGuid(), "Laboratory A", authorA, TimeProvider.System);
        var laboratoryB = new Laboratory(Guid.NewGuid(), "Laboratory B", authorB, TimeProvider.System);

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

        var laboratoryA = new Laboratory(Guid.NewGuid(), "Laboratory A", authorA, TimeProvider.System);
        var requiredLaboratory = new Laboratory(Guid.NewGuid(), "Required Laboratory", authorA, TimeProvider.System);

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

        var laboratory = new Laboratory(Guid.NewGuid(), "Laboratory A", authorId, TimeProvider.System);

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

        var laboratory = new Laboratory(Guid.NewGuid(), "Laboratory A", authorId, TimeProvider.System);

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

        var laboratoryA = new Laboratory(Guid.NewGuid(), "Laboratory A", authorA, TimeProvider.System);
        var laboratoryB = new Laboratory(Guid.NewGuid(), "Laboratory B", authorB, TimeProvider.System);

        await laboratories.AddAsync(laboratoryA);
        await laboratories.AddAsync(laboratoryB);

        using var client = factory.CreateClient();

        var requiredLaboratoryId = Guid.NewGuid();

        var response = await client.DeleteAsync(
            $"/api/laboratories/{laboratoryB.Id}/prerequisites/{requiredLaboratoryId}");

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }


    [Test]
    public async Task Author_CanReadOwnLaboratoryPrerequisites()
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
                            ["Identity:DevSubjectId"] = authorId.ToString(),
                            ["Authoring:AuthorAccountIds:0"] = authorId.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = new Laboratory(Guid.NewGuid(), "Laboratory A", authorId, TimeProvider.System);
        var required = new Laboratory(Guid.NewGuid(), "Required Laboratory", authorId, TimeProvider.System);

        await laboratories.AddAsync(laboratory);
        await laboratories.AddAsync(required);

        using var client = factory.CreateClient();

        using var addResponse = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites",
            new { RequiredLaboratoryId = required.Id });

        Assert.That(addResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.Created));

        using var response = await client.GetAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites");

        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var ids = await response.Content.ReadFromJsonAsync<Guid[]>();

        Assert.That(ids, Does.Contain(required.Id));
    }

    [Test]
    public async Task Author_CanRemoveOwnLaboratoryPrerequisite()
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
                            ["Identity:DevSubjectId"] = authorId.ToString(),
                            ["Authoring:AuthorAccountIds:0"] = authorId.ToString()
                        });
                });
            });

        var laboratories = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = new Laboratory(Guid.NewGuid(), "Laboratory A", authorId, TimeProvider.System);
        var required = new Laboratory(Guid.NewGuid(), "Required Laboratory", authorId, TimeProvider.System);

        await laboratories.AddAsync(laboratory);
        await laboratories.AddAsync(required);

        using var client = factory.CreateClient();

        using var addResponse = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites",
            new { RequiredLaboratoryId = required.Id });

        Assert.That(addResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.Created));

        using var deleteResponse = await client.DeleteAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites/{required.Id}");

        Assert.That(deleteResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));

        using var getResponse = await client.GetAsync(
            $"/api/laboratories/{laboratory.Id}/prerequisites");

        Assert.That(getResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var ids = await getResponse.Content.ReadFromJsonAsync<Guid[]>();

        Assert.That(ids, Does.Not.Contain(required.Id));
    }

}
