using Platform.Laboratories.Domain.Enums;

namespace Platform.Laboratories.Domain.Rules;

public static class RevisionConcurrency
{
    public static RevisionConcurrencyOutcome Evaluate(long expectedVersion, long currentVersion)
    {
        if (expectedVersion < 1 || currentVersion < 1)
            return RevisionConcurrencyOutcome.InvalidVersion;

        if (expectedVersion != currentVersion)
            return RevisionConcurrencyOutcome.ConcurrencyConflict;

        return RevisionConcurrencyOutcome.Allowed;
    }
}
