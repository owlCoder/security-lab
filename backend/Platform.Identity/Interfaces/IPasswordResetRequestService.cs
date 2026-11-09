using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IPasswordResetRequestService
{
    Task<PasswordResetRequestResult> RequestAsync(
        string email,
        CancellationToken cancellationToken = default);
}
