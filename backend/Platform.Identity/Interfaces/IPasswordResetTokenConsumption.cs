using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IPasswordResetTokenConsumption
{
    Task<PasswordResetTokenState> GetStateAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task<bool> TryConsumeAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
