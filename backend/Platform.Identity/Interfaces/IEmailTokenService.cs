using System;
using System.Collections.Generic;
using System.Text;
using Platform.Identity.Entities;

namespace Platform.Identity.Interfaces
{
    public interface IEmailTokenService
    {
        //Task<string> GenerateAsync(Guid userId);

        Task<bool> ValidateAsync(Guid userId, string token);

        Task<EmailVerificationToken> GenerateAsync(Guid userId);
    }
}
