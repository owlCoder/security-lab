using Platform.Api.Services.Identity;

namespace Platform.Api.Services.Identity;

internal static class EmailDeliveryLogging
{
    // SMTP exceptions can contain transport details; keep them out of logs because mail bodies carry bearer tokens.
    public static void ConfirmationFailed(
        ILogger logger,
        Guid? accountId,
        EmailDeliveryException _) =>
        logger.LogError("Email confirmation delivery failed for account {AccountId}.", accountId);

    public static void ConfirmationRequestFailed(
        ILogger logger,
        EmailDeliveryException _) =>
        logger.LogError("Email confirmation delivery failed.");

    public static void SessionNotificationFailed(
        ILogger logger,
        Guid accountId,
        EmailDeliveryException _) =>
        logger.LogError("New-session notification delivery failed for account {AccountId}.", accountId);
}
