using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IPasswordResetRedemptionService
{
    Task<PasswordResetRedemptionResult> RedeemAsync(
        string token,
        CancellationToken cancellationToken = default);
}
