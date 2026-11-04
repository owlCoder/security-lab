using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class AccountRegistrationService(UserManager<AppUser> userManager)
    : IAccountRegistration
{
    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 128;

    public async ValueTask<RegisterAccountResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var email = (command.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
        {
            return RegisterAccountResult.Invalid("Email adresa nije validna.");
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            return RegisterAccountResult.Invalid("Lozinka je obavezna.");
        }

        if (command.Password.Length > MaxPasswordLength)
        {
            return RegisterAccountResult.Invalid(
                $"Lozinka može imati najviše {MaxPasswordLength} karaktera.");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email
        };

        try
        {
            var result = await userManager.CreateAsync(user, command.Password);

            if (result.Succeeded)
            {
                return RegisterAccountResult.Created(user.Id);
            }

            if (result.Errors.Any(e =>
                    e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                           or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return RegisterAccountResult.Taken();
            }

            return RegisterAccountResult.Invalid(
                result.Errors.Select(e => e.Description).ToArray());
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Trka: dva istovremena zahteva za isti email, unique indeks u bazi je poslednja linija odbrane.
            return RegisterAccountResult.Taken();
        }
    }

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= MaxEmailLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;
}