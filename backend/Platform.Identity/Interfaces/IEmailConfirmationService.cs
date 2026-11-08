using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IEmailConfirmationService
{
    Task<EmailConfirmationGrant?> IssueAsync(string email, CancellationToken ct = default);

    Task<bool> ConfirmAsync(string token, CancellationToken ct = default);
}
