
using Microsoft.AspNetCore.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class PasswordChangeService(UserManager<AppUser> userManager)
    : IPasswordChangeService
{
    private const int MaxPasswordLength = 128;

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid authenticatedUserId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return await Task.FromCanceled<PasswordChangeResult>(cancellationToken);

        if (authenticatedUserId == Guid.Empty)
            return new(PasswordChangeOutcome.NotAuthenticated);

        if (currentPassword is not { Length: > 0 })
            return new(PasswordChangeOutcome.InvalidCurrentPassword);

        if (newPassword is not { Length: > 0 and <= MaxPasswordLength })
            return new(PasswordChangeOutcome.InvalidNewPassword);

        var user = await userManager.FindByIdAsync(authenticatedUserId.ToString());

        if (user is not { Status: AccountStatus.Active })
            return new(PasswordChangeOutcome.Failed);

        if (cancellationToken.IsCancellationRequested)
            return await Task.FromCanceled<PasswordChangeResult>(cancellationToken);

        var result = await userManager.ChangePasswordAsync(
            user,
            currentPassword,
            newPassword);

        if (result.Succeeded)
            return new(PasswordChangeOutcome.Success);

        foreach (var error in result.Errors)
        {
            if (error.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                return new(PasswordChangeOutcome.InvalidCurrentPassword);
        }

        foreach (var error in result.Errors)
        {
            if (error.Code.StartsWith("Password", StringComparison.Ordinal))
                return new(PasswordChangeOutcome.InvalidNewPassword);
        }

        return new(PasswordChangeOutcome.Failed);
    }
}
