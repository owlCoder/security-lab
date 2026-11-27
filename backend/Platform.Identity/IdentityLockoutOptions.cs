using Microsoft.Extensions.Options;

namespace Platform.Identity;

/// <summary>
/// IDN-05: privremeno zaključavanje naloga posle uzastopnih neuspešnih prijava.
/// Vrednosti se prenose u IdentityOptions.Lockout pri registraciji modula.
/// </summary>
public sealed class IdentityLockoutOptions
{
    public const string Section = "Identity:Lockout";

    public const int MinAttempts = 1;
    public const int MaxAttempts = 20;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 1440;

    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class IdentityLockoutOptionsValidator : IValidateOptions<IdentityLockoutOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityLockoutOptions options)
    {
        var errors = new List<string>();

        if (options.MaxFailedAccessAttempts is < IdentityLockoutOptions.MinAttempts or > IdentityLockoutOptions.MaxAttempts)
        {
            errors.Add(
                $"{IdentityLockoutOptions.Section}:MaxFailedAccessAttempts mora biti između " +
                $"{IdentityLockoutOptions.MinAttempts} i {IdentityLockoutOptions.MaxAttempts}.");
        }

        if (options.LockoutMinutes is < IdentityLockoutOptions.MinMinutes or > IdentityLockoutOptions.MaxMinutes)
        {
            errors.Add(
                $"{IdentityLockoutOptions.Section}:LockoutMinutes mora biti između " +
                $"{IdentityLockoutOptions.MinMinutes} i {IdentityLockoutOptions.MaxMinutes}.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
