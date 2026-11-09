namespace Platform.Identity.Models;

public sealed record PasswordResetRedemptionResult(
    bool IsSuccessful,
    string Message)
{
    public static PasswordResetRedemptionResult Success() =>
        new(true, "Reset token je uspesno iskoriscen.");

    public static PasswordResetRedemptionResult Rejected() =>
        new(false, "Reset token nije validan ili vise ne vazi.");
}
