namespace Platform.AssignmentPrerequisites.Contracts;

public sealed record AttemptOutcomeSnapshot(
    Guid AttemptId,
    Guid AssignmentId,
    string StudentId,
    AttemptState AttemptState,
    EvaluationStatus? EvaluationStatus,
    EvaluationOutcome? EvaluationOutcome);

public enum AttemptState
{
    Started = 1,
    InProgress = 2,
    Submitted = 3,
    InEvaluation = 4,
    AwaitingManualReview = 5,
    Completed = 6,
    Expired = 7,
    Cancelled = 8
}

public enum EvaluationStatus
{
    Pending = 1,
    Running = 2,
    ManualReview = 3,
    Completed = 4,
    TechnicalError = 5
}

public enum EvaluationOutcome
{
    Passed = 1,
    Failed = 2,
    Partial = 3
}
