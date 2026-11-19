using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Decorator over a real <see cref="ILoginHistorySource"/> that records how it was called, so tests
/// can prove that the source is not called for a rejected request and is called only for the caller.
/// </summary>
internal sealed class RecordingLoginHistorySource(ILoginHistorySource inner) : ILoginHistorySource
{
    private readonly List<string> _requestedSubjectIds = [];

    public int CallCount => _requestedSubjectIds.Count;

    public IReadOnlyList<string> RequestedSubjectIds => _requestedSubjectIds;

    public DateTimeOffset? LastFromUtc { get; private set; }

    public DateTimeOffset? LastToUtc { get; private set; }

    public int? LastSkip { get; private set; }

    public int? LastTake { get; private set; }

    public ValueTask<IReadOnlyList<LoginEvent>> FindAsync(
        string subjectId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        LoginOutcomeFilter? outcome,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        _requestedSubjectIds.Add(subjectId);
        LastFromUtc = fromUtc;
        LastToUtc = toUtc;
        LastSkip = skip;
        LastTake = take;

        return inner.FindAsync(subjectId, fromUtc, toUtc, outcome, skip, take, cancellationToken);
    }
}
