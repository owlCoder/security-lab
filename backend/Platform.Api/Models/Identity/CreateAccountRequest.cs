namespace Platform.Api.Models.Identity;

public sealed record CreateAccountRequest(string? Email, string? Password)
{
    public override string ToString() => "CreateAccountRequest { *** }";
}
