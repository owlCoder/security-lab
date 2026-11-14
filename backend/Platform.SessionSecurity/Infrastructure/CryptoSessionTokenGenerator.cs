using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Infrastructure;

/// <summary>
/// IDN-11: session tokens from the operating system CSPRNG (<see cref="RandomNumberGenerator"/>),
/// Base64Url encoded. Only the SHA-256 hash is meant to be stored, the same way as
/// <c>Platform.Identity</c> stores its session tokens.
/// </summary>
public sealed class CryptoSessionTokenGenerator : ISessionTokenGenerator
{
    // 256 bits of entropy, the same size as the Platform.Identity session token.
    private const int TokenSizeInBytes = 32;

    public IssuedSessionToken Generate()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));
        return new IssuedSessionToken(token, ComputeHash(token));
    }

    public string ComputeHash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
