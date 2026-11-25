using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Security.RevisionAuthorization;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;

namespace Platform.Laboratories.IntegrationTests;

public sealed partial class RevisionHttpWorkflowTests
{
    [TestCase("matching", HttpStatusCode.Created)]
    [TestCase("mismatch", HttpStatusCode.NotFound)]
    [TestCase("other-course-only", HttpStatusCode.Forbidden)]
    [TestCase("not-author", HttpStatusCode.NotFound)]
    [TestCase("unknown-course", HttpStatusCode.NotFound)]
    [TestCase("missing", HttpStatusCode.NotFound)]
    [TestCase("missing-policy", HttpStatusCode.Conflict)]
    public async Task CreateRevision_UsesPersistedLaboratoryCourse(string scenario, HttpStatusCode expected)
    {
        var requestedCourse = course;
        using (var scope = host.Services.CreateScope())
        {
            var labs = scope.ServiceProvider.GetRequiredService<LaboratoryDbContext>();
            var stored = await labs.Laboratories.SingleAsync(value => value.Id == lab);
            Assert.That(stored.CourseId, Is.EqualTo(course), "Course must survive the real PostgreSQL adapter.");
            if (scenario == "mismatch" || scenario == "other-course-only")
            {
                var otherCourse = Guid.NewGuid();
                var reviewDb = scope.ServiceProvider.GetRequiredService<
                    Platform.Laboratories.Infrastructure.Persistence.LaboratoriesDbContext>();
                reviewDb.CoursePublishPolicies.Add(((Platform.Laboratories.Domain.Models.CoursePublishPolicy.CreationResult.Success)
                    Platform.Laboratories.Domain.Models.CoursePublishPolicy.Create(otherCourse, true)).Value);
                await reviewDb.SaveChangesAsync();
                var security = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
                security.Permissions.Add(new RevisionCoursePermission
                {
                    UserId = author, CourseId = otherCourse, Action = RevisionActions.Create, IsActive = true
                });
                if (scenario == "mismatch")
                    requestedCourse = otherCourse;
                else
                    await security.Permissions.Where(value => value.UserId == author &&
                        value.CourseId == course && value.Action == RevisionActions.Create).ExecuteDeleteAsync();
                await security.SaveChangesAsync();
            }
            if (scenario == "unknown-course") stored.CourseId = null;
            if (scenario == "not-author") stored.CreatedBy = reviewer;
            if (scenario == "missing") labs.Laboratories.Remove(stored);
            if (scenario == "missing-policy")
            {
                var reviewDb = scope.ServiceProvider.GetRequiredService<
                    Platform.Laboratories.Infrastructure.Persistence.LaboratoriesDbContext>();
                await reviewDb.CoursePublishPolicies.Where(value => value.CourseId == course).ExecuteDeleteAsync();
            }
            await labs.SaveChangesAsync();
        }

        using var checkScope = host.Services.CreateScope();
        var revisions = checkScope.ServiceProvider.GetRequiredService<
            Platform.Laboratories.Infrastructure.Persistence.LaboratoriesDbContext>();
        var beforeRevisions = await revisions.LabRevisions.CountAsync();
        var beforeDefinitions = await revisions.ReviewDefinitions.CountAsync();
        using var response = await authorClient.PostAsJsonAsync("/api/revisions/", new
        {
            laboratoryId = lab, courseId = requestedCourse,
            definition = new { type = "LogAnalysis", title = "Course binding test" }
        });
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
        var added = expected == HttpStatusCode.Created ? 1 : 0;
        Assert.That(await revisions.LabRevisions.CountAsync(), Is.EqualTo(beforeRevisions + added));
        Assert.That(await revisions.ReviewDefinitions.CountAsync(), Is.EqualTo(beforeDefinitions + added));
        if (added == 1)
            Assert.That((await revisions.LabRevisions.SingleAsync(value => value.CourseId == course)).AuthorId,
                Is.EqualTo(author));
    }

    [Test]
    public async Task CreateRevision_UnavailableLaboratoryStore_DoesNotWriteRevisionOrDefinition()
    {
        using var scope = host.Services.CreateScope();
        var revisions = scope.ServiceProvider.GetRequiredService<
            Platform.Laboratories.Infrastructure.Persistence.LaboratoriesDbContext>();
        var beforeRevisions = await revisions.LabRevisions.CountAsync();
        var beforeDefinitions = await revisions.ReviewDefinitions.CountAsync();
        await ExecuteSql("ALTER TABLE authoring.laboratories RENAME TO laboratories_unavailable");
        try
        {
            using var response = await authorClient.PostAsJsonAsync("/api/revisions/", new
            {
                laboratoryId = lab, courseId = course,
                definition = new { type = "LogAnalysis", title = "Unavailable course source" }
            });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain("laboratory_unavailable"));
            Assert.That(body, Does.Not.Contain("Postgres").And.Not.Contain("SELECT"));
        }
        finally
        {
            await ExecuteSql("ALTER TABLE authoring.laboratories_unavailable RENAME TO laboratories");
        }
        Assert.That(await revisions.LabRevisions.CountAsync(), Is.EqualTo(beforeRevisions));
        Assert.That(await revisions.ReviewDefinitions.CountAsync(), Is.EqualTo(beforeDefinitions));
    }

    [Test]
    public async Task LaboratoryCourse_RoundTripsThroughGetListAndRename_AndCannotBeChangedByUpdate()
    {
        using (var scope = host.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>();
            var stored = await repository.GetByIdAsync(lab);
            Assert.That(stored!.CourseId, Is.EqualTo(course));
            stored.Rename("Renamed laboratory");
            await repository.UpdateAsync(stored);
        }
        using var readerScope = host.Services.CreateScope();
        var store = readerScope.ServiceProvider.GetRequiredService<
            Platform.LaboratoryPersistence.Application.ILaboratoryStore>();
        var row = await store.GetByIdAsync(lab);
        Assert.That(row!.CourseId, Is.EqualTo(course));
        await store.UpdateAsync(row with { CourseId = Guid.NewGuid(), Title = "Second rename" });
        using var finalScope = host.Services.CreateScope();
        var repositoryAfter = finalScope.ServiceProvider.GetRequiredService<ILaboratoryRepository>();
        Assert.That((await repositoryAfter.GetByIdAsync(lab))!.CourseId, Is.EqualTo(course));
        var listed = (await repositoryAfter.ListAsync()).Single(value => value.Id == lab);
        Assert.That(listed.CourseId, Is.EqualTo(course));
        Assert.That(listed.Title, Is.EqualTo("Second rename"));
    }
}
