using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Platform.Api.Interfaces.Identity;

namespace Platform.Api.Services.Identity;

public sealed class SmtpEmailSender(
    IConfiguration configuration,
    IHostEnvironment environment) : IEmailSender
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

        if (!enableSsl && !IsInsecureLocalMailpitAllowed(host, port, userName, password))
            throw new EmailDeliveryException(
                "Unencrypted SMTP is only allowed for explicitly configured local Mailpit in Development.");

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
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or ArgumentException or SocketException)
        {
            throw new EmailDeliveryException("The email could not be delivered.", ex);
        }
    }

    private bool IsInsecureLocalMailpitAllowed(
        string host,
        int port,
        string? userName,
        string? password)
    {
        if (!environment.IsDevelopment()
            || !configuration.GetValue("Email:Smtp:AllowInsecureLocalMailpit", false)
            || !string.IsNullOrEmpty(userName)
            || !string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
