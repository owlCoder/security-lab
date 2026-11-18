// IDN-22 TIM 09
namespace Platform.SessionSecurity.Domain;

/// <summary>
/// IDN-22: outcome of <see cref="LoginHistoryQueryValidator"/>. A valid result carries the
/// normalized <see cref="LoginHistorySearchWindow"/>; an invalid one carries only the code.
/// </summary>
public sealed record LoginHistoryQueryValidationResult
{
    private LoginHistoryQueryValidationResult(string code, LoginHistorySearchWindow? window)
    {
        Code = code;
        Window = window;
    }

    public bool IsValid => Window is not null;

    /// <summary>One of <see cref="LoginHistoryCodes"/>.</summary>
    public string Code { get; }

    public LoginHistorySearchWindow? Window { get; }

    public static LoginHistoryQueryValidationResult Valid(LoginHistorySearchWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new(LoginHistoryCodes.Ok, window);
    }

    public static LoginHistoryQueryValidationResult Invalid(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code == LoginHistoryCodes.Ok)
        {
            throw new ArgumentException("An invalid result cannot use the success code.", nameof(code));
        }

        return new(code, null);
    }
}
