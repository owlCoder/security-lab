using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Api.Modules.RevisionReviews;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.IntegrationTests;

public partial class PublicationDatabaseTests
{
    [TestCase("LogAnalysis", "event-1", 200)]
    [TestCase("LogAnalysis", null, 422)]
    [TestCase("LogAnalysis", "missing-event", 422)]
    [TestCase("Unknown", "event-1", 422)]
    public async Task Publish_UsesStoredDefinitionAndValidatesEvaluator(string type, string? answer, int expectedStatus)
    {
        var author = Guid.NewGuid();
        var course = Guid.NewGuid();
        // LAB-03: laboratorija sada zahteva kurs
        var laboratory = new Laboratory(course, "Log exercise", author);
        var repository = new InMemoryLaboratoryRepository();
        await repository.AddAsync(laboratory);
        await using var db = CreateContext();
        var policy = (CoursePublishPolicy.CreationResult.Success)CoursePublishPolicy.Create(course, true);
        db.CoursePublishPolicies.Add(policy.Value);
        await db.SaveChangesAsync();

        var factory = new ReviewContextFactory(new DbContextOptionsBuilder<
            Platform.Laboratories.Infrastructure.Persistence.LaboratoriesDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options);
        var access = new AllowReviewAccess();
        var drafts = new DraftRevisionService(factory, repository, access, TimeProvider.System);
        var definition = new PublicationDefinition(type, "Lab", "Goal",
            [new LogEventDefinition("event-1", DateTimeOffset.UnixEpoch, "login failed")], "What happened?",
            new EventSetConfiguration("event-set", 1, answer is null ? null : [answer]));
        var subject = new SubjectRef(author.ToString(), AccountState.Active);
        var created = await drafts.CreateAsync(laboratory.Id, course, author, subject, definition, "test", default);
        Assert.That(Status(created), Is.EqualTo(201));

        var revision = await db.LabRevisions.AsNoTracking().SingleAsync(value => value.CourseId == course);
        var service = new ReviewService(factory, access, new StoredPublicationSource(factory), TimeProvider.System,
            new Platform.Audit.Composition.PostgresTransactionalAuditWriter(TimeProvider.System));
        var submit = await Apply(service, revision.Id, author, new ReviewCommand("submit", 1, revision.ContentHash, "submit", null));
        Assert.That(Status(submit), Is.EqualTo(200));
        var detail = (ReviewDetail)((IValueHttpResult)submit).Value!;
        Assert.That(detail.Preview?.Question, Is.EqualTo("What happened?"));
        Assert.That(detail.Preview?.Events?.Single()?.Id, Is.EqualTo("event-1"));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(detail, System.Text.Json.JsonSerializerOptions.Web),
            Does.Not.Contain("expectedEventIds"));

        var approve = await Apply(service, revision.Id, Guid.NewGuid(),
            new ReviewCommand("approve", 2, revision.ContentHash, "approve", null));
        Assert.That(Status(approve), Is.EqualTo(expectedStatus));

        var publication = await db.ReviewOperations.SingleOrDefaultAsync(value =>
            value.RevisionId == revision.Id && value.Action == "approve");
        var saved = await db.LabRevisions.AsNoTracking().SingleAsync(value => value.Id == revision.Id);
        if (expectedStatus == 200)
        {
            Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.Published));
            Assert.That(publication?.Number, Is.EqualTo(1));
            Assert.That(publication?.Snapshot, Is.EqualTo(definition.Serialize()));

            var next = definition with { Question = "Which event shows a failed login?" };
            Assert.That(Status(await drafts.CreateAsync(laboratory.Id, course, author, subject, next, "test", default)),
                Is.EqualTo(201));
            var second = await db.LabRevisions.AsNoTracking().SingleAsync(value =>
                value.CourseId == course && value.Id != revision.Id);
            Assert.That(Status(await Apply(service, second.Id, author,
                new ReviewCommand("submit", 1, second.ContentHash, "submit", null))), Is.EqualTo(200));
            Assert.That(Status(await Apply(service, second.Id, Guid.NewGuid(),
                new ReviewCommand("approve", 2, second.ContentHash, "approve", null))), Is.EqualTo(200));

            var published = await db.ReviewOperations.AsNoTracking().Where(value => value.LaboratoryId == laboratory.Id)
                .OrderBy(value => value.Number).ToArrayAsync();
            Assert.That(published.Select(value => value.Number), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(published[0].Snapshot, Is.EqualTo(definition.Serialize()));
            Assert.That(published[1].Snapshot, Is.EqualTo(next.Serialize()));
        }
        else
        {
            Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.InReview));
            Assert.That(publication, Is.Null);
        }
    }
}
