using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Domain.Models;

public sealed class RevisionDecisionRecord
{
    public Guid Id { get; private set; }
    public Guid RevisionId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public RevisionReviewDecision Decision { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    public string ContentHash { get; private set; }
    public bool RequiresIndependentApproval { get; private set; }

    private RevisionDecisionRecord(
        Guid id,
        Guid revisionId,
        Guid reviewerId,
        RevisionReviewDecision decision,
        DateTimeOffset decidedAt,
        string contentHash,
        bool requiresIndependentApproval)
    {
        Id = id;
        RevisionId = revisionId;
        ReviewerId = reviewerId;
        Decision = decision;
        DecidedAt = decidedAt.ToUniversalTime();
        ContentHash = contentHash;
        RequiresIndependentApproval = requiresIndependentApproval;
    }

    public static CreationResult Create(
        Guid id,
        Guid revisionId,
        Guid reviewerId,
        RevisionReviewDecision decision,
        DateTimeOffset decidedAt,
        string contentHash,
        bool requiresIndependentApproval)
    {
        if (id == Guid.Empty)
            return new CreationResult.Failure(CreationError.InvalidId);

        if (revisionId == Guid.Empty)
            return new CreationResult.Failure(CreationError.InvalidRevisionId);

        if (reviewerId == Guid.Empty)
            return new CreationResult.Failure(CreationError.InvalidReviewerId);

        if (decision is not (RevisionReviewDecision.Approved or RevisionReviewDecision.ReturnedForChanges))
            return new CreationResult.Failure(CreationError.InvalidDecision);

        if (decidedAt == default)
            return new CreationResult.Failure(CreationError.InvalidDecisionTime);

        if (!RevisionContentHash.IsValid(contentHash))
            return new CreationResult.Failure(CreationError.InvalidContentHash);

        return new CreationResult.Success(new RevisionDecisionRecord(
            id, revisionId, reviewerId, decision, decidedAt,
            contentHash, requiresIndependentApproval));
    }

    public enum CreationError
    {
        InvalidId = 1,
        InvalidRevisionId = 2,
        InvalidReviewerId = 3,
        InvalidDecision = 4,
        InvalidDecisionTime = 5,
        InvalidContentHash = 6
    }

    public abstract record CreationResult
    {
        private CreationResult() { }

        public sealed record Success(RevisionDecisionRecord Value) : CreationResult;
        public sealed record Failure(CreationError Error) : CreationResult;
    }
}
