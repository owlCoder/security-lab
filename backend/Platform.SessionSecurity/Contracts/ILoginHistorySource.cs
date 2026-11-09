using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: reads stored login events. The subject filter is part of the query itself, so events
/// of other users are never loaded (SEC-01). Results are ordered by time, newest first.
/// </summary>
public interface ILoginHistorySource
{
    ValueTask<IReadOnlyList<LoginEvent>> FindAsync(
        string subjectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        LoginOutcomeFilter? outcome,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
