using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Contracts;

/// <summary>
/// IDN-11: outcome of a rotation. <see cref="NewToken"/> is set only on success and is meant
/// for the host that writes the session cookie; every rejection gives the client the same
/// generic answer.
/// </summary>
public sealed record SessionRotationResult
{
    private SessionRotationResult(bool succeeded, string code, string? newToken)
    {
        Succeeded = succeeded;
        Code = code;
        NewToken = newToken;
    }

    public bool Succeeded { get; }

    /// <summary>One of <see cref="SessionRotationCodes"/>.</summary>
    public string Code { get; }

    public string? NewToken { get; }

    public static SessionRotationResult Rotated(string newToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newToken);
        return new(true, SessionRotationCodes.Rotated, newToken);
    }

    public static SessionRotationResult Rejected(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code == SessionRotationCodes.Rotated)
        {
            throw new ArgumentException("A rejection cannot use the success code.", nameof(code));
        }

        return new(false, code, null);
    }

    // AUD-03: never print the new session token.
    public override string ToString() =>
        $"SessionRotationResult {{ Succeeded = {Succeeded}, Code = {Code} }}";
}
