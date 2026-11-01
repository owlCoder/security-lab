namespace Platform.Api.Modules.Identity.RateLimiting;

/// <summary>
/// Imena politika za ograničenje broja pokušaja (IDN-05, SEC-04).
/// Endpoint za prijavu (IDN-02) i reset lozinke (IDN-04) se samo označe:
/// kontroler: <c>[EnableRateLimiting(RateLimitPolicies.Login)]</c> na akciji;
/// minimal API: <c>.RequireRateLimiting(RateLimitPolicies.Login)</c>.
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "identity-login";
    public const string PasswordReset = "identity-password-reset";
}
