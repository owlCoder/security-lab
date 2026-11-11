
using Microsoft.AspNetCore.Http;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Contracts;
using Microsoft.AspNetCore.Hosting;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;
using System.Net;
using System.Net.Http.Json;

namespace Laboratories.Tests;

internal sealed class TestMetadataSubjectProvider : IAccessSubjectProvider
{
    public const string HeaderName = "X-LAB02-Test-User";

    public ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!httpContext.Request.Headers.TryGetValue(
                HeaderName, out var values))
        {
            return ValueTask.FromResult<SubjectRef?>(null);
        }

        if (values.Count != 1 ||
            !Guid.TryParse(values[0], out var userId) ||
            userId == Guid.Empty)
        {
            return ValueTask.FromResult<SubjectRef?>(null);
        }

        return ValueTask.FromResult<SubjectRef?>(
            new SubjectRef(
                userId.ToString(),
                AccountState.Active));
    }
}


internal static class MetadataTestData
{
    public static readonly Guid CourseId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static readonly Guid OtherCourseId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static readonly Guid LaboratoryId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public static readonly Guid TeacherId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid StudentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid OtherTeacherId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static void ConfigureAccess(
        InMemoryMembershipReader memberships,
        InMemoryResourceDescriptorResolver resources)
    {
        memberships.Set(new CourseMembership(
            TeacherId,
            CourseId,
            CourseRole.Teacher,
            MembershipStatus.Active,
            new HashSet<string>()));

        memberships.Set(new CourseMembership(
            StudentId,
            CourseId,
            CourseRole.Student,
            MembershipStatus.Active,
            new HashSet<string>()));

        memberships.Set(new CourseMembership(
            OtherTeacherId,
            OtherCourseId,
            CourseRole.Teacher,
            MembershipStatus.Active,
            new HashSet<string>()));

        resources.SetCourse(
            new ResourceRef(
                "laboratory",
                LaboratoryId.ToString()),
            CourseId);
    }
}


internal static class MetadataTestHost
{
    public static WebApplicationFactory<Program> Create()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Development");

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAccessSubjectProvider>();
                    services.AddScoped<
                        IAccessSubjectProvider,
                        TestMetadataSubjectProvider>();


                    services.RemoveAll<IMembershipReader>();
                    services.AddSingleton<InMemoryMembershipReader>();
                    services.AddSingleton<IMembershipReader>(sp =>
                        sp.GetRequiredService<InMemoryMembershipReader>());

                    services.RemoveAll<IResourceDescriptorResolver>();
                    services.AddSingleton<InMemoryResourceDescriptorResolver>();
                    services.AddSingleton<IResourceDescriptorResolver>(sp =>
                        sp.GetRequiredService<InMemoryResourceDescriptorResolver>());

                });
            });
    }
}


[TestFixture]
public sealed class LaboratoryMetadataApiTests
{
    [Test]
    public async Task CourseTeacher_CanUpdateLaboratoryMetadata()
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();

        var memberships = factory.Services
            .GetRequiredService<InMemoryMembershipReader>();

        var resources = factory.Services
            .GetRequiredService<InMemoryResourceDescriptorResolver>();

        MetadataTestData.ConfigureAccess(memberships, resources);


        var laboratoryId = MetadataTestData.LaboratoryId;

        var laboratoryRepository = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = Laboratory.Rehydrate(
            laboratoryId,
            MetadataTestData.CourseId,
            "Test laboratorija",
            LabStatus.Draft,
            MetadataTestData.TeacherId,
            DateTimeOffset.UtcNow);

        await laboratoryRepository.AddAsync(laboratory);

        var savedLaboratory =
            await laboratoryRepository.GetByIdAsync(laboratoryId);

        Assert.That(savedLaboratory, Is.Not.Null);

        var metadataRepository = factory.Services
    .GetRequiredService<ILaboratoryMetadataRepository>();


        var existingMetadata = new LaboratoryMetadata(
            laboratoryId,
            "Pocetni cilj laboratorije",
            LaboratoryDifficulty.Easy,
            LaboratoryType.AccessControl,
            new List<string> { "osnove" });


        await metadataRepository.SaveAsync(existingMetadata);

        var savedMetadata =
            await metadataRepository.GetByLaboratoryIdAsync(laboratoryId);

        Assert.That(savedMetadata, Is.Not.Null);


        client.DefaultRequestHeaders.Add(
            TestMetadataSubjectProvider.HeaderName,
            MetadataTestData.TeacherId.ToString());

        var response = await client.PatchAsJsonAsync(
            $"/api/laboratories/{laboratoryId}/metadata",
            new
            {
                Objective = "Izmenjeni cilj laboratorije",
                Difficulty = LaboratoryDifficulty.Medium,
                Type = LaboratoryType.AccessControl,
                Tags = new[] { "bezbednost", "autorizacija" }
            });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updatedMetadata =
            await metadataRepository.GetByLaboratoryIdAsync(laboratoryId);

        Assert.That(updatedMetadata, Is.Not.Null);
        Assert.That(
            updatedMetadata!.Objective,
            Is.EqualTo("Izmenjeni cilj laboratorije"));

    }


    [Test]
    public async Task Student_CannotUpdateLaboratoryMetadata()
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();

        var memberships = factory.Services
            .GetRequiredService<InMemoryMembershipReader>();

        var resources = factory.Services
            .GetRequiredService<InMemoryResourceDescriptorResolver>();

        MetadataTestData.ConfigureAccess(memberships, resources);

        var laboratoryId = MetadataTestData.LaboratoryId;

        client.DefaultRequestHeaders.Add(
            TestMetadataSubjectProvider.HeaderName,
            MetadataTestData.StudentId.ToString());

        var response = await client.PatchAsJsonAsync(
            $"/api/laboratories/{laboratoryId}/metadata",
            new
            {
                Objective = "Student pokusava izmenu",
                Difficulty = LaboratoryDifficulty.Medium,
                Type = LaboratoryType.AccessControl,
                Tags = new[] { "test" }
            });

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }


    [Test]
    public async Task TeacherOfAnotherCourse_CannotUpdateLaboratoryMetadata()
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();

        var memberships = factory.Services
            .GetRequiredService<InMemoryMembershipReader>();

        var resources = factory.Services
            .GetRequiredService<InMemoryResourceDescriptorResolver>();

        MetadataTestData.ConfigureAccess(memberships, resources);

        client.DefaultRequestHeaders.Add(
            TestMetadataSubjectProvider.HeaderName,
            MetadataTestData.OtherTeacherId.ToString());

        var response = await client.PatchAsJsonAsync(
            $"/api/laboratories/{MetadataTestData.LaboratoryId}/metadata",
            new
            {
                Objective = "Unauthorized update",
                Difficulty = LaboratoryDifficulty.Medium,
                Type = LaboratoryType.AccessControl,
                Tags = new[] { "test" }
            });

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }


    [Test]
    public async Task CourseTeacher_CanCreateLaboratoryMetadata()
    {
        using var factory = MetadataTestHost.Create();
        using var client = factory.CreateClient();

        var memberships = factory.Services
            .GetRequiredService<InMemoryMembershipReader>();

        var resources = factory.Services
            .GetRequiredService<InMemoryResourceDescriptorResolver>();

        MetadataTestData.ConfigureAccess(memberships, resources);

        var laboratoryId = MetadataTestData.LaboratoryId;

        var laboratoryRepository = factory.Services
            .GetRequiredService<ILaboratoryRepository>();

        var laboratory = Laboratory.Rehydrate(
            laboratoryId,
            MetadataTestData.CourseId,
            "Test laboratorija za POST",
            LabStatus.Draft,
            MetadataTestData.TeacherId,
            DateTimeOffset.UtcNow);

        await laboratoryRepository.AddAsync(laboratory);

        client.DefaultRequestHeaders.Add(
            TestMetadataSubjectProvider.HeaderName,
            MetadataTestData.TeacherId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/laboratories/{laboratoryId}/metadata",
            new
            {
                Objective = "Nauciti osnove kontrole pristupa",
                Difficulty = LaboratoryDifficulty.Easy,
                Type = LaboratoryType.AccessControl,
                Tags = new[] { "bezbednost", "osnove" }
            });

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.Created));

        var metadataRepository = factory.Services
            .GetRequiredService<ILaboratoryMetadataRepository>();

        var savedMetadata = await metadataRepository
            .GetByLaboratoryIdAsync(laboratoryId);

        Assert.That(savedMetadata, Is.Not.Null);

        Assert.That(
            savedMetadata!.Objective,
            Is.EqualTo("Nauciti osnove kontrole pristupa"));
    }
}

