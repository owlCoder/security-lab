
using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IPasswordChangeService
{
    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid authenticatedUserId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}
