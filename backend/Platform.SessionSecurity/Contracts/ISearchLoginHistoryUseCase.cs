// IDN-22 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-22: searches the login history of the caller only, with masked sensitive values.
/// </summary>
public interface ISearchLoginHistoryUseCase
{
    ValueTask<LoginHistoryResult> SearchAsync(
        AuthenticatedSubject? subject,
        SearchLoginHistoryQuery? query,
        CancellationToken cancellationToken = default);
}
