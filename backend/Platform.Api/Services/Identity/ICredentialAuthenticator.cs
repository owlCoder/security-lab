using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
namespace Platform.Api.Services.Identity;

public enum CredentialAuthenticationOutcome
{
    Valid,
    Invalid,
    Unavailable
}

public sealed record CredentialAuthenticationResult(
    CredentialAuthenticationOutcome Outcome,
    Guid? AccountId = null);

/// <summary>
/// IDN-02 boundary: verifies the supplied credentials and returns the account id
/// only after successful verification. Implementations must not log the password.
/// </summary>
public interface ICredentialAuthenticator
{
    CredentialAuthenticationResult Authenticate(string email, string password);
}
