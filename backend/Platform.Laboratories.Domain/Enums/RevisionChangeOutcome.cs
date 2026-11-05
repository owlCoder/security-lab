namespace Platform.Laboratories.Domain.Enums;

public enum RevisionChangeOutcome
{
    NotEvaluated = 0,
    Applied = 1,
    InvalidInput = 2,
    ConcurrencyConflict = 3,
    ContentChanged = 4,
    InvalidTransition = 5,
    SelfApprovalForbidden = 6,
    PolicyMismatch = 7,
    VersionLimitReached = 8,
    RevisionNotFound = 9,
    PolicyMissing = 10,
    StorageFailure = 11
}
