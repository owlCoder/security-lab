namespace Platform.Identity.Models;

public sealed record RegisterAccountCommand(string Email, string Password)
{
    public override string ToString() => "RegisterAccountCommand { *** }";
}