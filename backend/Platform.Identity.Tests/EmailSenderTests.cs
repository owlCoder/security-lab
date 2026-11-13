using Microsoft.Extensions.Configuration;
using Platform.Identity.Services;

namespace Platform.Identity.Tests
{
    [TestFixture]
    public class EmailSenderTests
    {
        private static EmailSender CreateEmailSender()
        {
            var settings = new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "localhost",
                ["Smtp:Port"] = "1025",
                ["Smtp:FromEmail"] = "slab@securitylab.test"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new EmailSender(configuration);
        }
        [Test]
        [Explicit("Requires local Mailpit SMTP server")]
        public async Task SendVerificationEmailAsync_SendEmail()
        {
            var sender = CreateEmailSender();

            string email = "123@temp.com";
            string link = "https://temp.com/verify?token=123";

            await sender.SendVerificationEmailAsync(email, link);

            Assert.Pass("Email was accepted by the SMTP server.");
        }   
    }
}
