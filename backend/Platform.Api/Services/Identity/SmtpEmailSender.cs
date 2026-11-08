using System.Net;
using System.Net.Mail;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var host = configuration["Email:Smtp:Host"];
        var fromAddress = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
            throw new EmailDeliveryException("Email delivery is not configured.");

        var port = configuration.GetValue("Email:Smtp:Port", 587);
        var enableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true);
        var userName = configuration["Email:Smtp:User"];
        var password = configuration["Email:Smtp:Password"];

        try
        {
            using var message = new MailMessage(fromAddress, recipient, subject, body);
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrEmpty(userName))
                client.Credentials = new NetworkCredential(userName, password);

            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or ArgumentException)
        {
            throw new EmailDeliveryException("The email could not be delivered.", ex);
        }
    }
}
