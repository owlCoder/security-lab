using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Platform.Identity.Services
{
    public class EmailTokenService : IEmailTokenService
    {
        public Task<EmailVerificationToken> GenerateAsync(Guid userId)
        {
            var token = new EmailVerificationToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsUsed = false
            };

            return Task.FromResult(token);
        }

        public Task<bool> ValidateAsync(Guid userId, string token)
        {
            return Task.FromResult(false);
        }
    }
}
