using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Tests;

public class RevisionConcurrencyTests
{
    [TestCase(1L, 1L, RevisionConcurrencyOutcome.Allowed)]
    [TestCase(3L, 3L, RevisionConcurrencyOutcome.Allowed)]
    [TestCase(long.MaxValue, long.MaxValue, RevisionConcurrencyOutcome.Allowed)]
    [TestCase(3L, 4L, RevisionConcurrencyOutcome.ConcurrencyConflict)]
    [TestCase(4L, 3L, RevisionConcurrencyOutcome.ConcurrencyConflict)]
    [TestCase(0L, 1L, RevisionConcurrencyOutcome.InvalidVersion)]
    [TestCase(1L, 0L, RevisionConcurrencyOutcome.InvalidVersion)]
    [TestCase(-1L, 1L, RevisionConcurrencyOutcome.InvalidVersion)]
    [TestCase(1L, -1L, RevisionConcurrencyOutcome.InvalidVersion)]
    [TestCase(0L, 0L, RevisionConcurrencyOutcome.InvalidVersion)]
    public void Evaluate_ReturnsExpectedOutcome(
        long expectedVersion,
        long currentVersion,
        RevisionConcurrencyOutcome expected)
    {
        var result = RevisionConcurrency.Evaluate(expectedVersion, currentVersion);

        Assert.That(result, Is.EqualTo(expected));
    }
}
