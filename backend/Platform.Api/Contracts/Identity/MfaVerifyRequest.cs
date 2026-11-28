namespace Platform.Api.Contracts.Identity;

public sealed record MfaVerifyRequest(string Code)
{
    // At Trace level MVC logs action arguments with ToString(), and the generated record ToString() prints every
    // property. Keep the TOTP or backup code out of the log (US-17, US-18, AUD-03), as LoginRequest does.
    public override string ToString() => "MfaVerifyRequest { *** }";
}
