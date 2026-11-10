// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: a new session token and its hash. <see cref="Value"/> goes only to the client cookie;
/// only <see cref="Hash"/> is stored.
/// </summary>
public sealed record IssuedSessionToken(string Value, string Hash)
{
    // AUD-03: the token must never appear in logs, exceptions or test output.
    public override string ToString() => "IssuedSessionToken { (hidden) }";
}
