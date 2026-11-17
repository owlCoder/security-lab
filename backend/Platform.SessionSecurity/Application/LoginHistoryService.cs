using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.SessionSecurity.Application;

/// <summary>
/// IDN-22: searches the personal login history of the caller.
/// SEC-01: whose events are read comes only from <see cref="AuthenticatedSubject"/>, which the host
/// builds from a checked session; the query has no user field, and the subject filter is applied
/// by <see cref="ILoginHistorySource"/> itself, so events of other users are never loaded.
/// SEC-04 / SEC-05: the filter is validated before the source is called.
/// AUD-03: source addresses are masked on the server and no subject ID, token or session value
/// is returned.
/// </summary>
public sealed class LoginHistoryService(ILoginHistorySource source, TimeProvider timeProvider)
    : ISearchLoginHistoryUseCase
{
    private readonly ILoginHistorySource _source =
        source ?? throw new ArgumentNullException(nameof(source));

    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<LoginHistoryResult> SearchAsync(
        AuthenticatedSubject? subject,
        SearchLoginHistoryQuery? query,
        CancellationToken cancellationToken = default)
    {
        if (subject is null || !subject.IsAuthenticated)
        {
            return LoginHistoryResult.Rejected(LoginHistoryCodes.NotAuthenticated);
        }

        if (string.IsNullOrWhiteSpace(subject.SubjectId) || query is null)
        {
            return LoginHistoryResult.Rejected(LoginHistoryCodes.InvalidRequest);
        }

        var validation = LoginHistoryQueryValidator.Validate(
            query.FromUtc, query.ToUtc, query.Outcome, query.Page, query.PageSize, _timeProvider.GetUtcNow());

        if (validation.Window is not { } window)
        {
            return LoginHistoryResult.Rejected(validation.Code);
        }

        var events = await _source.FindAsync(
            subject.SubjectId,
            window.FromUtc,
            window.ToUtc,
            window.Outcome,
            window.Skip,
            window.PageSize,
            cancellationToken);

        return LoginHistoryResult.Ok(events.Select(ToHistoryItem).ToArray(), window.Page, window.PageSize);
    }

    private static LoginHistoryItem ToHistoryItem(LoginEvent loginEvent) =>
        new(loginEvent.OccurredAt, loginEvent.Succeeded, SourceAddressMasker.Mask(loginEvent.SourceAddress));
}
