using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Domain.Enums;
using Platform.Access.AspNet;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;
using Platform.Prerequisites.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class LabAccessHttpTests
{
    [Test]
    public async Task Student_WithoutPrerequisites_CanAccessLaboratory()
    {
        var studentId = Guid.NewGuid();
        var laboratoryId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        using var factory = CreateFactory(studentId);
        SeedPublishedLaboratory(factory, laboratoryId, courseId, studentId);

        var membership = factory.Services
            .GetRequiredService<InMemoryCourseMembership>();

        membership.Set(courseId, studentId, Platform.LabVisibility.Contracts.CourseRole.Student);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/laboratories/{laboratoryId}/access");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<AccessResponse>();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsUnlocked, Is.True);
        Assert.That(result.MissingPrerequisites, Is.Empty);
    }

    [Test]
    public async Task Student_WithMissingPrerequisites_IsLocked()
    {
        var studentId = Guid.NewGuid();
        var laboratoryId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var missingPrerequisiteId = Guid.NewGuid();

        using var factory = CreateFactory(studentId);
        SeedPublishedLaboratory(factory, laboratoryId, courseId, studentId);

        var membership = factory.Services
            .GetRequiredService<InMemoryCourseMembership>();

        membership.Set(courseId, studentId, Platform.LabVisibility.Contracts.CourseRole.Student);

        var prerequisiteRepository =
            factory.Services.GetRequiredService<IPrerequisiteRepository>();

        var addResult = await prerequisiteRepository.AddAsync(
            new LabPrerequisite(laboratoryId, missingPrerequisiteId));

        Assert.That(addResult, Is.EqualTo(AddPrerequisiteResult.Added));

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/laboratories/{laboratoryId}/access");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<AccessResponse>();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsUnlocked, Is.False);
        Assert.That(result.MissingPrerequisites, Does.Contain(missingPrerequisiteId));
    }

    [Test]
    public async Task AnonymousUser_CannotAccessLaboratory()
    {
        var laboratoryId = Guid.NewGuid();

        using var factory = CreateFactory(null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/laboratories/{laboratoryId}/access");

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Student_WhenPrerequisiteStorageUnavailable_Returns503()
    {
        var studentId = Guid.NewGuid();
        var laboratoryId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        using var factory = CreateFactory(studentId, prerequisiteStorageUnavailable: true);
        using var scope = factory.Services.CreateScope();

        var contentReader = scope.ServiceProvider
            .GetRequiredService<InMemoryLabPublicContentReader>();

        var membership = scope.ServiceProvider
            .GetRequiredService<InMemoryCourseMembership>();

        contentReader.Save(LabPublicContent.Create(
            laboratoryId,
            courseId,
            Guid.NewGuid(),
            LabVisibilityState.Published,
            "Test laboratorija",
            "Test cilj",
            Array.Empty<PublicTask>()));

        membership.Set(
            courseId,
            studentId,
            Platform.LabVisibility.Contracts.CourseRole.Student);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/laboratories/{laboratoryId}/access");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.That(body?.Code, Is.EqualTo("prerequisite_storage_unavailable"));
    }

    private sealed record ErrorResponse(string Code);

    private static WebApplicationFactory<Program> CreateFactory(Guid? subjectId,
            bool prerequisiteStorageUnavailable = false)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessSubjectProvider>();

                services.AddSingleton<IAccessSubjectProvider>(
                    new TestAccessSubjectProvider(subjectId));

                services.RemoveAll<IRequestSubjectResolver>();

                services.AddSingleton<IRequestSubjectResolver>(
                    new TestRequestSubjectResolver(subjectId));
                if (prerequisiteStorageUnavailable)
                {
                    services.RemoveAll<IPrerequisiteRepository>();
                    services.AddSingleton<IPrerequisiteRepository, UnavailablePrerequisiteRepository>();
                }
            });
        });
    }
    private sealed class TestAccessSubjectProvider(Guid? subjectId)
    : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            if (subjectId is null)
                return ValueTask.FromResult<SubjectRef?>(null);

            return ValueTask.FromResult<SubjectRef?>(
                new SubjectRef(
                    subjectId.Value.ToString(),
                    AccountState.Active));
        }
    }

    private static void SeedPublishedLaboratory(
        WebApplicationFactory<Program> factory,
        Guid laboratoryId,
        Guid courseId,
        Guid authorId)
    {
        var contentReader = factory.Services
            .GetRequiredService<InMemoryLabPublicContentReader>();

        var content = LabPublicContent.Create(
            laboratoryId,
            courseId,
            authorId,
            LabVisibilityState.Published,
            "Test laboratory",
            "Test goal",
            Array.Empty<PublicTask>());

        contentReader.Save(content);
    }

    private sealed record AccessResponse(
        bool IsUnlocked,
        IReadOnlyList<Guid> MissingPrerequisites);

    private sealed class TestRequestSubjectResolver(Guid? subjectId)
    : IRequestSubjectResolver
    {
        public SubjectRef? Resolve(HttpContext context)
        {
            if (subjectId is null)
                return null;

            return new SubjectRef(
                subjectId.Value.ToString(),
                AccountState.Active);
        }
    }
}