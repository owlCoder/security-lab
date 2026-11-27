
namespace Platform.Identity.Models;

public enum PasswordChangeOutcome
{
    Success,
    NotAuthenticated,
    InvalidCurrentPassword,
    InvalidNewPassword,
    Failed
}

public sealed record PasswordChangeResult(
    PasswordChangeOutcome Outcome)
{
    public bool IsSuccessful =>
        Outcome == PasswordChangeOutcome.Success;
}
