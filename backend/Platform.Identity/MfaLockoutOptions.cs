using Microsoft.Extensions.Options;

namespace Platform.Identity;

/// <summary>
/// US-18: lockout of the second factor after wrong TOTP codes, separate from the password lockout (IDN-05,
/// <see cref="IdentityLockoutOptions"/>), so a change to the password settings does not change the MFA lockout.
/// The limits are stricter than for a password: a 6-digit code with a window of one step on each side has three
/// valid values, so the most the limits allow (10 codes every 15 minutes, 960 a day) gives an attacker about a
/// 0.3% chance a day per account. The defaults (5 every 15 minutes) give about 0.14%.
/// </summary>
public sealed class MfaLockoutOptions
{
    public const string Section = "Identity:Mfa:Lockout";

    public const int MinAttempts = 1;
    public const int MaxAttempts = 10;
    public const int MinMinutes = 15;
    public const int MaxMinutes = 1440;

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class MfaLockoutOptionsValidator : IValidateOptions<MfaLockoutOptions>
{
    public ValidateOptionsResult Validate(string? name, MfaLockoutOptions options)
    {
        var errors = new List<string>();

        if (options.MaxFailedAttempts is < MfaLockoutOptions.MinAttempts or > MfaLockoutOptions.MaxAttempts)
        {
            errors.Add(
                $"{MfaLockoutOptions.Section}:MaxFailedAttempts must be between " +
                $"{MfaLockoutOptions.MinAttempts} and {MfaLockoutOptions.MaxAttempts}.");
        }

        if (options.LockoutMinutes is < MfaLockoutOptions.MinMinutes or > MfaLockoutOptions.MaxMinutes)
        {
            errors.Add(
                $"{MfaLockoutOptions.Section}:LockoutMinutes must be between " +
                $"{MfaLockoutOptions.MinMinutes} and {MfaLockoutOptions.MaxMinutes}.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
