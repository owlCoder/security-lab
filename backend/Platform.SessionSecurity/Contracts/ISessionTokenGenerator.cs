// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: creates unpredictable session tokens. Implementations must use a cryptographically
/// secure random number generator, never <c>Random</c>, <c>Guid</c> or the current time.
/// </summary>
public interface ISessionTokenGenerator
{
    IssuedSessionToken Generate();

    /// <summary>Hash of a token received from the client, used to look the session up.</summary>
    string ComputeHash(string token);
}
