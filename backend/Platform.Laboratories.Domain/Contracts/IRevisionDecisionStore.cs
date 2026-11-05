using Platform.Laboratories.Domain.Enums;

namespace Platform.Laboratories.Domain.Contracts;

public sealed record RevisionDecisionRequest(
    Guid RevisionId,
    Guid ReviewerId,
    long ExpectedVersion,
    string ExpectedContentHash,
    RevisionReviewDecision Decision);

public abstract record DecisionSaveResult
{
    private DecisionSaveResult() { }
    public sealed record Success(Guid DecisionId, long RowVersion, LabRevisionStatus Status) : DecisionSaveResult;
    public sealed record Failure(RevisionChangeOutcome Reason) : DecisionSaveResult;
}

public interface IRevisionDecisionStore
{
    Task<DecisionSaveResult> SaveDecisionAsync(
        RevisionDecisionRequest request,
        CancellationToken cancellationToken = default);
}
