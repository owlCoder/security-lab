using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.TestDoubles;

/// <summary>
/// Uses the real CSPRNG generator and remembers every issued token, so a test can check a token
/// that the service never returned (for example after a failed audit).
/// </summary>
public sealed class RecordingTokenGenerator : ISessionTokenGenerator
{
    private readonly CryptoSessionTokenGenerator inner = new();
    private readonly List<IssuedSessionToken> issued = [];

    public IReadOnlyList<IssuedSessionToken> Issued => issued;

    public IssuedSessionToken Generate()
    {
        var token = inner.Generate();
        issued.Add(token);
        return token;
    }

    public string ComputeHash(string token) => inner.ComputeHash(token);
}
