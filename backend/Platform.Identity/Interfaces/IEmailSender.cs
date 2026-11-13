namespace Platform.Identity.Interfaces
{
    public interface IEmailSender
    {
        Task SendVerificationEmailAsync(string email, string verificationLink, CancellationToken cancellationToken = default);
    }
}
