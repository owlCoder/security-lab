namespace Platform.Identity.Interfaces;

public interface ISessionNotificationPreferences
{
    Task<bool?> GetAsync(Guid accountId, CancellationToken ct = default);

    Task<bool> SetAsync(Guid accountId, bool enabled, CancellationToken ct = default);
}
