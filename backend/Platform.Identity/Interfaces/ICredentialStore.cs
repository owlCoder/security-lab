using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>Finds the data needed for a login check. The e-mail passed in is already normalized.</summary>
public interface ICredentialStore
{
    Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>
    /// IDN-05: charges one attempt before the password is checked, so parallel requests cannot all pass.
    /// An attempt that finds <paramref name="maxAttempts"/> already charged sets <paramref name="lockoutEnd"/>
    /// and restarts the counter. A locked account or an unknown e-mail changes nothing.
    /// </summary>
    Task ReserveAttemptAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd, CancellationToken ct = default);

    /// <summary>
    /// IDN-05: finishes a failed attempt. Sets <paramref name="lockoutEnd"/> and restarts the counter when
    /// <paramref name="maxAttempts"/> are charged. A locked account or an unknown e-mail changes nothing.
    /// </summary>
    /// <returns>The lockout end stored after the update, null when there is none.</returns>
    Task<DateTimeOffset?> LockIfLimitReachedAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd, CancellationToken ct = default);

    /// <summary>IDN-05: finishes a correct password. Resets the counter unless the account is locked by now.</summary>
    /// <returns>False when the account is locked, so the login must be refused.</returns>
    Task<bool> TryClearFailuresAsync(Guid accountId, DateTimeOffset now, CancellationToken ct = default);
}
