using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Laboratories.Domain.Contracts;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Laboratories.IntegrationTests;

public partial class PublicationDatabaseTests
{
    private async Task<(Guid RevisionId, Guid AuthorId)> SeedReview(bool independent = true, bool includePolicy = true)
    {
        var courseId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var creation = LabRevision.Create(revisionId, courseId, authorId, ValidHash);
        Assert.That(creation, Is.TypeOf<LabRevision.CreationResult.Success>());
        var revision = ((LabRevision.CreationResult.Success)creation).Value;
        Assert.That(revision.SubmitForReview(1), Is.EqualTo(RevisionChangeOutcome.Applied));
        await using var context = CreateContext();
        if (includePolicy)
        {
            var policy = CoursePublishPolicy.Create(courseId, independent);
            Assert.That(policy, Is.TypeOf<CoursePublishPolicy.CreationResult.Success>());
            context.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)policy).Value);
        }
        context.LabRevisions.Add(revision);
        await context.SaveChangesAsync();
        return (revisionId, authorId);
    }

    private RevisionDecisionStore CreateStore(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<LaboratoriesDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", LaboratoriesDbContext.SchemaName))
            .AddInterceptors(interceptors).Options;
        return new RevisionDecisionStore(new ReviewContextFactory(options), TimeProvider.System);
    }

    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    public async Task Approval_AppliesCoursePolicyAndSavesHistoryAtomically(
        bool independent, bool isAuthor, bool allowed)
    {
        var review = await SeedReview(independent);
        var reviewerId = isAuthor ? review.AuthorId : Guid.NewGuid();
        var result = await CreateStore().SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, reviewerId, 2, ValidHash, RevisionReviewDecision.Approved));

        await using var reader = CreateContext();
        var saved = await reader.LabRevisions.SingleAsync(value => value.Id == review.RevisionId);
        var history = await reader.RevisionDecisions.Where(value => value.RevisionId == review.RevisionId).ToListAsync();
        if (allowed)
        {
            Assert.That(result, Is.TypeOf<DecisionSaveResult.Success>());
            Assert.Multiple(() =>
            {
                Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.Published));
                Assert.That(saved.RowVersion, Is.EqualTo(3));
                Assert.That(history, Has.Count.EqualTo(1));
                Assert.That(history[0].ReviewerId, Is.EqualTo(reviewerId));
                Assert.That(history[0].ContentHash, Is.EqualTo(ValidHash));
                Assert.That(history[0].RequiresIndependentApproval, Is.EqualTo(independent));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(new DecisionSaveResult.Failure(RevisionChangeOutcome.SelfApprovalForbidden)));
                Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.InReview));
                Assert.That(saved.RowVersion, Is.EqualTo(2));
                Assert.That(history, Is.Empty);
            });
        }
    }

    [Test]
    public async Task ReturnForChanges_SavesDraftStateAndDecision()
    {
        var review = await SeedReview();
        var result = await CreateStore().SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, Guid.NewGuid(), 2, ValidHash, RevisionReviewDecision.ReturnedForChanges));
        await using var reader = CreateContext();
        var saved = await reader.LabRevisions.SingleAsync(value => value.Id == review.RevisionId);
        var history = await reader.RevisionDecisions.SingleAsync(value => value.RevisionId == review.RevisionId);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<DecisionSaveResult.Success>());
            Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.Draft));
            Assert.That(saved.RowVersion, Is.EqualTo(3));
            Assert.That(history.Decision, Is.EqualTo(RevisionReviewDecision.ReturnedForChanges));
        });
    }

    [TestCase("version", RevisionChangeOutcome.ConcurrencyConflict)]
    [TestCase("hash", RevisionChangeOutcome.ContentChanged)]
    [TestCase("policy", RevisionChangeOutcome.PolicyMissing)]
    public async Task Decision_WithStaleOrMissingData_LeavesStateAndHistoryUnchanged(
        string invalidField, RevisionChangeOutcome expected)
    {
        var review = await SeedReview(includePolicy: invalidField != "policy");
        var hash = invalidField == "hash" ? new string('a', 64) : ValidHash;
        var version = invalidField == "version" ? 1 : 2;
        var result = await CreateStore().SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, Guid.NewGuid(), version, hash, RevisionReviewDecision.Approved));
        await using var reader = CreateContext();
        var saved = await reader.LabRevisions.SingleAsync(value => value.Id == review.RevisionId);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new DecisionSaveResult.Failure(expected)));
            Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.InReview));
            Assert.That(saved.RowVersion, Is.EqualTo(2));
        });
        Assert.That(await reader.RevisionDecisions.AnyAsync(value => value.RevisionId == review.RevisionId), Is.False);
    }

    [Test]
    public async Task ConcurrentDecisions_OnlyOneCommitsStateAndHistory()
    {
        var review = await SeedReview();
        var store = CreateStore(new ConcurrentSaveGate());
        var first = store.SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, Guid.NewGuid(), 2, ValidHash, RevisionReviewDecision.Approved));
        var second = store.SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, Guid.NewGuid(), 2, ValidHash, RevisionReviewDecision.ReturnedForChanges));

        var results = await Task.WhenAll(first, second);

        Assert.That(results.Count(value => value is DecisionSaveResult.Success), Is.EqualTo(1));
        Assert.That(results.Count(value => value == new DecisionSaveResult.Failure(RevisionChangeOutcome.ConcurrencyConflict)), Is.EqualTo(1));
        var winner = (DecisionSaveResult.Success)results.Single(value => value is DecisionSaveResult.Success);
        await using var reader = CreateContext();
        var saved = await reader.LabRevisions.SingleAsync(value => value.Id == review.RevisionId);
        var history = await reader.RevisionDecisions.Where(value => value.RevisionId == review.RevisionId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.RowVersion, Is.EqualTo(3));
            Assert.That(saved.Status, Is.EqualTo(winner.Status));
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Id, Is.EqualTo(winner.DecisionId));
        });
    }

    [Test]
    public async Task FailedHistoryInsert_RollsBackRevisionChange()
    {
        var review = await SeedReview();
        var store = CreateStore(new InvalidHistoryWrite());
        var result = await store.SaveDecisionAsync(new RevisionDecisionRequest(
            review.RevisionId, Guid.NewGuid(), 2, ValidHash, RevisionReviewDecision.Approved));
        await using var reader = CreateContext();
        var saved = await reader.LabRevisions.SingleAsync(value => value.Id == review.RevisionId);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new DecisionSaveResult.Failure(RevisionChangeOutcome.StorageFailure)));
            Assert.That(saved.Status, Is.EqualTo(LabRevisionStatus.InReview));
            Assert.That(saved.RowVersion, Is.EqualTo(2));
        });
        Assert.That(await reader.RevisionDecisions.AnyAsync(value => value.RevisionId == review.RevisionId), Is.False);
    }

    private sealed class ReviewContextFactory(DbContextOptions<LaboratoriesDbContext> options)
        : IDbContextFactory<LaboratoriesDbContext>
    {
        public LaboratoriesDbContext CreateDbContext() => new(options);
    }

    private sealed class ConcurrentSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed class InvalidHistoryWrite : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is LaboratoriesDbContext context)
            {
                foreach (var entry in context.ChangeTracker.Entries<RevisionDecisionRecord>())
                    entry.Property(value => value.Decision).CurrentValue = (RevisionReviewDecision)99;
            }
            return ValueTask.FromResult(result);
        }
    }
}
