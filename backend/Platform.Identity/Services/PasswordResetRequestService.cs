using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class PasswordResetRequestService(
    ICredentialStore credentialStore,
    IPasswordResetTokenIssuer tokenIssuer,
    ILogger<PasswordResetRequestService> logger)
    : IPasswordResetRequestService
{
    private const int MaxEmailLength = 254;

    public async Task<PasswordResetRequestResult> RequestAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (email is not { Length: > 0 and <= MaxEmailLength })
        {
            return PasswordResetRequestResult.InvalidEmail();
        }

        var normalizedEmail = CredentialVerifier.NormalizeEmail(email);

        if (normalizedEmail.Length == 0 ||
            !MailAddress.TryCreate(normalizedEmail, out var parsed) || 
            !string.Equals(parsed.Address, normalizedEmail, StringComparison.Ordinal))
        {
            return PasswordResetRequestResult.InvalidEmail();
        }

        try
        {
            var account = await credentialStore.FindByEmailAsync(
                normalizedEmail, cancellationToken);

            if (account is { Status: AccountStatus.Active })
            {
                await tokenIssuer.IssueAsync(
                    account.AccountId, cancellationToken);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Password reset request could not be processed.");
        }

        return PasswordResetRequestResult.Accepted();
    }
}
