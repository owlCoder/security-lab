using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Domain.Models;

public sealed class LabRevision
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid AuthorId { get; private set; }
    public LabRevisionStatus Status { get; private set; }
    public string ContentHash { get; private set; }
    public long RowVersion { get; private set; }

    private LabRevision(Guid id, Guid courseId, Guid authorId, string contentHash)
    {
        Id = id;
        CourseId = courseId;
        AuthorId = authorId;
        ContentHash = contentHash;
        Status = LabRevisionStatus.Draft;
        RowVersion = 1;
    }

    public static CreationResult Create(Guid id, Guid courseId, Guid authorId, string contentHash)
    {
        if (id == Guid.Empty || courseId == Guid.Empty || authorId == Guid.Empty ||
            !RevisionContentHash.IsValid(contentHash))
            return new CreationResult.Failure(RevisionChangeOutcome.InvalidInput);

        return new CreationResult.Success(new LabRevision(id, courseId, authorId, contentHash));
    }

    public RevisionChangeOutcome SubmitForReview(long expectedVersion)
    {
        return ChangeStatus(LabRevisionStatus.InReview, expectedVersion);
    }

    public RevisionChangeOutcome Archive(long expectedVersion)
    {
        return ChangeStatus(LabRevisionStatus.Archived, expectedVersion);
    }

    public RevisionChangeOutcome Decide(
        Guid reviewerId,
        long expectedVersion,
        string expectedContentHash,
        RevisionReviewDecision decision,
        CoursePublishPolicy policy)
    {
        if (reviewerId == Guid.Empty || !RevisionContentHash.IsValid(expectedContentHash) ||
            decision is not (RevisionReviewDecision.Approved or RevisionReviewDecision.ReturnedForChanges))
            return RevisionChangeOutcome.InvalidInput;

        var versionCheck = CheckVersion(expectedVersion);
        if (versionCheck != RevisionChangeOutcome.Applied)
            return versionCheck;

        if (!string.Equals(expectedContentHash, ContentHash, StringComparison.Ordinal))
            return RevisionChangeOutcome.ContentChanged;

        if (policy is null)
            return RevisionChangeOutcome.PolicyMissing;

        if (policy.CourseId != CourseId)
            return RevisionChangeOutcome.PolicyMismatch;

        var target = decision == RevisionReviewDecision.Approved
            ? LabRevisionStatus.Published
            : LabRevisionStatus.Draft;
        if (!LabRevisionTransitions.CanTransition(Status, target) || Status != LabRevisionStatus.InReview)
            return RevisionChangeOutcome.InvalidTransition;

        if (decision == RevisionReviewDecision.Approved &&
            policy.EvaluateApproval(reviewerId == AuthorId) != RevisionApprovalOutcome.Allowed)
            return RevisionChangeOutcome.SelfApprovalForbidden;

        Status = target;
        RowVersion++;
        return RevisionChangeOutcome.Applied;
    }

    private RevisionChangeOutcome ChangeStatus(LabRevisionStatus target, long expectedVersion)
    {
        var versionCheck = CheckVersion(expectedVersion);
        if (versionCheck != RevisionChangeOutcome.Applied)
            return versionCheck;

        if (!LabRevisionTransitions.CanTransition(Status, target))
            return RevisionChangeOutcome.InvalidTransition;

        Status = target;
        RowVersion++;
        return RevisionChangeOutcome.Applied;
    }

    private RevisionChangeOutcome CheckVersion(long expectedVersion)
    {
        var result = RevisionConcurrency.Evaluate(expectedVersion, RowVersion);
        if (result == RevisionConcurrencyOutcome.InvalidVersion)
            return RevisionChangeOutcome.InvalidInput;
        if (result != RevisionConcurrencyOutcome.Allowed)
            return RevisionChangeOutcome.ConcurrencyConflict;
        if (RowVersion == long.MaxValue)
            return RevisionChangeOutcome.VersionLimitReached;
        return RevisionChangeOutcome.Applied;
    }

    public abstract record CreationResult
    {
        private CreationResult() { }
        public sealed record Success(LabRevision Value) : CreationResult;
        public sealed record Failure(RevisionChangeOutcome Reason) : CreationResult;
    }
}
