namespace Platform.Identity.Interfaces;

public interface IPasswordResetTokenIssuer
{
    Task IssueAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
