using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Infrastructure;

/// <summary>
/// IDN-22: in-memory <see cref="ILoginHistorySource"/> for tests and the KT1 demo. It holds an
/// immutable snapshot of synthetic events. Like a database query, it filters by subject, time
/// range (both bounds inclusive) and outcome, orders newest first (stable) and returns one page.
/// </summary>
public sealed class InMemoryLoginHistorySource : ILoginHistorySource
{
    private readonly IReadOnlyList<LoginEvent> _events;

    public InMemoryLoginHistorySource(IEnumerable<LoginEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var snapshot = events.ToArray();
        if (snapshot.Any(loginEvent => loginEvent is null))
        {
            throw new ArgumentException("Login events must not contain null.", nameof(events));
        }

        _events = snapshot;
    }

    public ValueTask<IReadOnlyList<LoginEvent>> FindAsync(
        string subjectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        LoginOutcomeFilter? outcome,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        IReadOnlyList<LoginEvent> page = _events
            .Where(loginEvent => string.Equals(loginEvent.SubjectId, subjectId, StringComparison.Ordinal))
            .Where(loginEvent => loginEvent.OccurredAt >= fromUtc && loginEvent.OccurredAt <= toUtc)
            .Where(loginEvent => MatchesOutcome(loginEvent, outcome))
            .OrderByDescending(loginEvent => loginEvent.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToArray();

        return ValueTask.FromResult(page);
    }

    private static bool MatchesOutcome(LoginEvent loginEvent, LoginOutcomeFilter? outcome) => outcome switch
    {
        null => true,
        LoginOutcomeFilter.Succeeded => loginEvent.Succeeded,
        LoginOutcomeFilter.Failed => !loginEvent.Succeeded,
        _ => false,
    };
}
