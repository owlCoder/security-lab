using Microsoft.Extensions.Logging;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class PasswordResetRedemptionService(
    IPasswordResetTokenHasher tokenHasher,
    IPasswordResetTokenConsumption tokenConsumption,
    TimeProvider timeProvider,
    ILogger<PasswordResetRedemptionService> logger)
    : IPasswordResetRedemptionService
{
    public async Task<PasswordResetRedemptionResult> RedeemAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (token is not { Length: > 0 })
        {
            return PasswordResetRedemptionResult.Rejected();
        }

        try
        {
            if (!tokenHasher.TryComputeHash(token, out var tokenHash) ||
                tokenHash.Length == 0)
            {
                return PasswordResetRedemptionResult.Rejected();
            }

            var now = timeProvider.GetUtcNow();

            var state = await tokenConsumption.GetStateAsync(
                tokenHash,
                cancellationToken);

            if (!state.Exists ||
                state.ExpiresAt <= state.CreatedAt ||
                now < state.CreatedAt ||
                now >= state.ExpiresAt ||
                state.IsUsed)
            {
                return PasswordResetRedemptionResult.Rejected();
            }

            var consumed = await tokenConsumption.TryConsumeAsync(
                tokenHash,
                now,
                cancellationToken);

            return consumed
                ? PasswordResetRedemptionResult.Success()
                : PasswordResetRedemptionResult.Rejected();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Password reset token redemption could not be completed.");

            return PasswordResetRedemptionResult.Rejected();
        }
    }
}
