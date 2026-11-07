using Platform.Identity.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Platform.Identity.Tests.Services
{
    [TestFixture]
    public class EmailTokenServiceTests
    {
        [Test]
        public async Task GenerateAsync_ShouldGenerateToken()
        {
            var service = new EmailTokenService();

            var result = await service.GenerateAsync(Guid.NewGuid());

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Token, Is.Not.Empty);
            Assert.That(result.IsUsed, Is.False);
            Assert.That(result.ExpiresAt, Is.GreaterThan(DateTime.UtcNow));

        }

    }
}
