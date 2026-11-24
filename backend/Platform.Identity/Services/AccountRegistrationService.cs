using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

/// <summary>
/// IDN-01: an administrator creates an account in identity.accounts, the same store that login (IDN-02),
/// account status (IDN-03) and OIDC federation (IDN-08) use, so the new account can log in immediately.
/// The e-mail is unique (ux_accounts_email) and accounts are never deleted, so an identifier is never reused.
/// </summary>
public sealed class AccountRegistrationService(
    PlatformIdentityDbContext db,
    IPasswordHasher passwordHasher) : IAccountRegistration
{
    private const int MaxEmailLength = 254;
    internal const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128; // bounds the cost of hashing
    private const string EmailIndex = "ux_accounts_email";

    public async ValueTask<RegisterAccountResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // Same normalization as login (CredentialVerifier), so the new account is found when it logs in.
        var email = CredentialVerifier.NormalizeEmail(command.Email ?? string.Empty);
        if (!IsValidEmail(email))
            return RegisterAccountResult.Invalid("Email adresa nije validna.");

        var passwordErrors = ValidatePassword(command.Password);
        if (passwordErrors.Length > 0)
            return RegisterAccountResult.Invalid(passwordErrors);

        // Blocked and archived accounts keep their e-mail, so it is never given to someone else.
        if (await db.Accounts.AsNoTracking().AnyAsync(account => account.Email == email, cancellationToken))
            return RegisterAccountResult.Taken();

        var created = new UserAccount(Guid.NewGuid(), email, AccountStatus.Active, AccountRole.Student);
        created.SetPasswordHash(passwordHasher.Hash(command.Password));
        db.Accounts.Add(created);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RegisterAccountResult.Created(created.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                               ConstraintName: EmailIndex
                                           })
        {
            // Two concurrent requests for the same e-mail: the unique index decides, the loser gets 409.
            db.ChangeTracker.Clear();
            return RegisterAccountResult.Taken();
        }
    }

    /// <summary>Password policy (min. 12, upper and lower case letter, digit, special character).</summary>
    internal static string[] ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return ["Lozinka je obavezna."];
        if (password.Length > MaxPasswordLength)
            return [$"Lozinka može imati najviše {MaxPasswordLength} karaktera."];

        var errors = new List<string>();
        if (password.Length < MinPasswordLength)
            errors.Add($"Lozinka mora imati najmanje {MinPasswordLength} karaktera.");
        if (!password.Any(char.IsUpper))
            errors.Add("Lozinka mora sadržati bar jedno veliko slovo.");
        if (!password.Any(char.IsLower))
            errors.Add("Lozinka mora sadržati bar jedno malo slovo.");
        if (!password.Any(char.IsDigit))
            errors.Add("Lozinka mora sadržati bar jedan broj.");
        if (password.All(char.IsLetterOrDigit))
            errors.Add("Lozinka mora sadržati bar jedan specijalan znak.");
        return [.. errors];
    }

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= MaxEmailLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;
}