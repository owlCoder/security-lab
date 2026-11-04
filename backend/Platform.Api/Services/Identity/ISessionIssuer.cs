using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
namespace Platform.Api.Services.Identity;

public sealed record SessionIssueResult(bool Succeeded, string? AccessToken = null);

/// <summary>
/// IDN-02 boundary for creating a session after the account has passed the
/// server-side ACTIVE status check.
/// </summary>
public interface ISessionIssuer
{
    SessionIssueResult Issue(Guid accountId);
}
