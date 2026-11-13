using Microsoft.Extensions.Configuration;
using Platform.Identity.Interfaces;
using System.Net;
using System.Net.Mail;

namespace Platform.Identity.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task SendVerificationEmailAsync(string email, string verificationLink, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string host = _configuration["Smtp:Host"]
                ?? throw new InvalidOperationException("SMTP host is missing.");
            
            int port = int.Parse(
                _configuration["Smtp:Port"]
                ?? throw new InvalidOperationException("SMTP port is missing."));

            string fromEmail = _configuration["Smtp:FromEmail"]
                ?? throw new InvalidOperationException("SMTP sender is missing.");

            string message = CreateVerificationMessage(verificationLink);

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail),
                Subject = "Verify Your Email Address",
                Body = message,
                IsBodyHtml = false
            };

            mailMessage.To.Add(email);

            using var smtpClient = new SmtpClient(host, port)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                EnableSsl = false
            };

            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
        }

        public string CreateVerificationMessage(string verificationLink)
        {
            return $"""
                Welcome to Security Lab!

                To verify your email address,
                please click the following link:

                {verificationLink}

                If you did not create an account,
                please ignore this email.
                """;
        }
    }
}
