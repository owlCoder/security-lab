using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.RevisionReviews;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Laboratories.IntegrationTests;

public partial class PublicationDatabaseTests
{
    [Test]
    public async Task Approve_ConcurrentRetry_PublishesOneSnapshotAndOneDecision()
    {
        var (id, _) = await SeedReview();
        var actor = Guid.NewGuid();
        var lab = Guid.NewGuid();
        var source = new SnapshotSource(new PublicationCandidate(lab, "abc"u8.ToArray(), []));
        var service = CreateWorkflow(source);
        var command = new ReviewCommand("approve", 2, ValidHash, "approve-1", null);

        var results = await Task.WhenAll(Apply(service, id, actor, command), Apply(service, id, actor, command));
        Assert.That(results.Select(Status), Is.All.EqualTo(200));

        var conflict = await Apply(service, id, actor, command with { Hash = new string('f', 64) });
        Assert.That(Status(conflict), Is.EqualTo(409));

        await using var reader = CreateContext();
        var receipt = await reader.ReviewOperations.SingleAsync(value => value.RevisionId == id);
        var revision = await reader.LabRevisions.SingleAsync(value => value.Id == id);
        var decisions = await reader.RevisionDecisions.CountAsync(value => value.RevisionId == id);
        Assert.Multiple(() =>
        {
            Assert.That(revision.Status, Is.EqualTo(LabRevisionStatus.Published));
            Assert.That(revision.RowVersion, Is.EqualTo(3));
            Assert.That(receipt.Snapshot, Is.EqualTo("abc"u8.ToArray()));
            Assert.That(receipt.Number, Is.EqualTo(1));
            Assert.That(decisions, Is.EqualTo(1));
        });
    }

    [TestCase(true, 422)]
    [TestCase(false, 409)]
    public async Task Approve_InvalidOrChangedSnapshot_DoesNotPublish(bool invalid, int expectedStatus)
    {
        var (id, _) = await SeedReview();
        var source = new SnapshotSource(new PublicationCandidate(Guid.NewGuid(), "different"u8.ToArray(),
            invalid ? [new PublicationError("evaluator", "Incomplete evaluator")] : []));
        var result = await Apply(CreateWorkflow(source), id, Guid.NewGuid(),
            new ReviewCommand("approve", 2, ValidHash, "approve-2", null));

        await using var reader = CreateContext();
        var revision = await reader.LabRevisions.SingleAsync(value => value.Id == id);
        Assert.That(Status(result), Is.EqualTo(expectedStatus));
        Assert.That(revision.Status, Is.EqualTo(LabRevisionStatus.InReview));
        Assert.That(await reader.ReviewOperations.AnyAsync(value => value.RevisionId == id), Is.False);
        Assert.That(await reader.RevisionDecisions.AnyAsync(value => value.RevisionId == id), Is.False);
    }

    [Test]
    public async Task ReturnThenSubmit_KeepsCommentAndRejectsStaleRetryWithNewKey()
    {
        var (id, author) = await SeedReview();
        var reviewer = Guid.NewGuid();
        var service = CreateWorkflow(new SnapshotSource(null));
        var returned = new ReviewCommand("return", 2, ValidHash, "return-1", "Popravi šemu");
        Assert.That(Status(await Apply(service, id, reviewer, returned)), Is.EqualTo(200));
        Assert.That(Status(await Apply(service, id, author,
            new ReviewCommand("submit", 3, ValidHash, "submit-1", null))), Is.EqualTo(200));
        Assert.That(Status(await Apply(service, id, reviewer, returned with { Key = "stale" })), Is.EqualTo(409));

        await using var reader = CreateContext();
        var history = await reader.ReviewOperations.Where(value => value.RevisionId == id).OrderBy(value => value.Version).ToListAsync();
        Assert.That(history.Select(value => value.Action), Is.EqualTo(new[] { "return", "submit" }));
        Assert.That(history[0].Comment, Is.EqualTo("Popravi šemu"));
    }

    private ReviewService CreateWorkflow(IPublicationSource source)
    {
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;
        return new ReviewService(new ReviewContextFactory(options), new AllowReviewAccess(), source, TimeProvider.System);
    }

    private static Task<IResult> Apply(ReviewService service, Guid id, Guid actor, ReviewCommand command) =>
        service.ApplyAsync(id, actor, new SubjectRef(actor.ToString(), AccountState.Active), command, "test", default);

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private sealed class SnapshotSource(PublicationCandidate? candidate) : IPublicationSource
    {
        public Task<PublicationCandidate?> LoadAndValidateAsync(Guid revisionId, CancellationToken cancellationToken) =>
            Task.FromResult(candidate);
    }

    private sealed class AllowReviewAccess : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AccessDecision.Allow("test", request.CorrelationId));
    }
}
