using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Domain.Contracts;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Infrastructure.Persistence;

public sealed class RevisionDecisionStore(
    IDbContextFactory<LaboratoriesDbContext> contexts,
    TimeProvider clock) : IRevisionDecisionStore
{
    public async Task<DecisionSaveResult> SaveDecisionAsync(
        RevisionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.RevisionId == Guid.Empty || request.ReviewerId == Guid.Empty ||
            request.ExpectedVersion < 1 || !RevisionContentHash.IsValid(request.ExpectedContentHash) ||
            request.Decision is not (RevisionReviewDecision.Approved or RevisionReviewDecision.ReturnedForChanges))
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.InvalidInput);

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var revision = await context.LabRevisions.SingleOrDefaultAsync(
            value => value.Id == request.RevisionId, cancellationToken);
        if (revision is null)
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.RevisionNotFound);

        var policy = await context.CoursePublishPolicies
            .FromSqlInterpolated($"""
                SELECT "CourseId", "RequiresIndependentApproval"
                FROM laboratories."CoursePublishPolicies"
                WHERE "CourseId" = {revision.CourseId}
                FOR SHARE
                """)
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (policy is null)
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.PolicyMissing);

        var outcome = revision.Decide(request.ReviewerId, request.ExpectedVersion,
            request.ExpectedContentHash, request.Decision, policy);
        if (outcome != RevisionChangeOutcome.Applied)
            return new DecisionSaveResult.Failure(outcome);

        var recordResult = RevisionDecisionRecord.Create(
            Guid.NewGuid(), revision.Id, request.ReviewerId, request.Decision,
            clock.GetUtcNow(), revision.ContentHash, policy.RequiresIndependentApproval);
        if (recordResult is not RevisionDecisionRecord.CreationResult.Success created)
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.InvalidInput);

        context.RevisionDecisions.Add(created.Value);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.ConcurrencyConflict);
        }
        catch (DbUpdateException)
        {
            return new DecisionSaveResult.Failure(RevisionChangeOutcome.StorageFailure);
        }

        return new DecisionSaveResult.Success(created.Value.Id, revision.RowVersion, revision.Status);
    }
}
