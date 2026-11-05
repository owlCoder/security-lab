namespace Platform.Laboratories.Domain.Enums;

public enum RevisionConcurrencyOutcome
{
    NotEvaluated = 0,
    Allowed = 1,
    InvalidVersion = 2,
    ConcurrencyConflict = 3
}
