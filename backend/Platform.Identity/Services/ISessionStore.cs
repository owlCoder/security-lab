using Platform.Identity.Domain;

internal interface ISessionStore
{
	Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct);

    Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct);
}
