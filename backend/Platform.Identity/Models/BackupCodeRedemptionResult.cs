namespace Platform.Identity.Models;

public sealed record BackupCodeRedemptionResult(
    bool IsSuccessful,
    string Message)
{
    public static BackupCodeRedemptionResult Success() =>
        new(true, "Rezervni MFA kod je uspesno iskoriscen.");

    public static BackupCodeRedemptionResult Rejected() =>
        new(false, "Rezervni MFA kod nije validan ili vise ne vazi.");
}
