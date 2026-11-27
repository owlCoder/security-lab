using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

public sealed class AccountRegistrationService(PlatformIdentityDbContext db, IPasswordHasher hasher)
    : IAccountRegistration
{
    private const int MinPasswordLength = 12;
    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 128;

    public async ValueTask<RegisterAccountResult> RegisterAsync(
        RegisterAccountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var email = (command.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return RegisterAccountResult.Invalid("Email adresa nije validna.");

        var errors = PasswordErrors(command.Password);
        if (errors.Length > 0)
            return RegisterAccountResult.Invalid(errors);

        if (await db.Accounts.AnyAsync(a => a.Email == email, cancellationToken))
            return RegisterAccountResult.Taken();

        var account = new UserAccount(Guid.NewGuid(), email, AccountStatus.Active, AccountRole.Student);
        account.SetPasswordHash(hasher.Hash(command.Password));
        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RegisterAccountResult.Created(account.Id);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Trka: unique indeks ux_accounts_email je poslednja linija odbrane.
            return RegisterAccountResult.Taken();
        }
    }

    private static string[] PasswordErrors(string? password)
    {
        if (string.IsNullOrEmpty(password)) return ["Lozinka je obavezna."];
        if (password.Length > MaxPasswordLength)
            return [$"Lozinka može imati najviše {MaxPasswordLength} karaktera."];

        var errors = new List<string>();
        if (password.Length < MinPasswordLength) errors.Add($"Lozinka mora imati najmanje {MinPasswordLength} karaktera.");
        if (!password.Any(char.IsUpper)) errors.Add("Lozinka mora sadržati veliko slovo.");
        if (!password.Any(char.IsLower)) errors.Add("Lozinka mora sadržati malo slovo.");
        if (!password.Any(char.IsDigit)) errors.Add("Lozinka mora sadržati broj.");
        if (password.All(char.IsLetterOrDigit)) errors.Add("Lozinka mora sadržati specijalan znak.");
        return errors.ToArray();
    }

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= MaxEmailLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;
}