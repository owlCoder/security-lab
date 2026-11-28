namespace Platform.Api.Contracts.Identity;

public sealed record LoginRequest(string? Email, string? Password)
{
    // At Trace level MVC logs action arguments with ToString(), and the generated record ToString() prints every
    // property. Keep the typed e-mail and password out of the log (AUD-03, SEC-02), as CreateAccountRequest does.
    public override string ToString() => "LoginRequest { *** }";
}
