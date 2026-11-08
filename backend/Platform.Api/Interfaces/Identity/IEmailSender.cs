namespace Platform.Api.Interfaces.Identity;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default);
}
