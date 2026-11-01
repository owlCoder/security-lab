using Platform.Identity.Domain;

public interface ISessionStore
{
	Task<(string RawToken, Session Session)> CreateAsync(
        Guid userId, AuthLevel level, TimeSpan lifetime, CancellationToken ct);

    Task<Session?> FindByTokenAsync(string rawToken, CancellationToken ct);

    Task TouchAsync(Session session, DateTimeOffset now, CancellationToken ct);
}
