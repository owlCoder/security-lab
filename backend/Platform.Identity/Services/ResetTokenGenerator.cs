using System.Security.Cryptography;
using System.Text;
using Platform.Identity.Interfaces;

namespace Platform.Identity.Services;

public sealed class ResetTokenGenerator : IResetTokenGenerator
{
    private const int TokenByteCount = 32;
    private const int TokenLength = 43;
    private const int HashLength = 64;

    public string GenerateToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(TokenByteCount);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public bool TryComputeHash(string token, out string tokenHash)
    {
        tokenHash = string.Empty;

        if (!IsValidToken(token))
        {
            return false;
        }

        byte[] bytes = Encoding.ASCII.GetBytes(token);
        byte[] hash = SHA256.HashData(bytes);

        tokenHash = Convert.ToHexString(hash);
        return true;
    }

    public bool VerifyHash(string token, string storedHash)
    {
        if (storedHash is not { Length: HashLength })
        {
            return false;
        }

        if (!TryComputeHash(token, out string computedHash))
        {
            return false;
        }

        byte[] computedBytes = Encoding.ASCII.GetBytes(computedHash);
        byte[] storedBytes = Encoding.ASCII.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(
            computedBytes,
            storedBytes);
    }

    private static bool IsValidToken(string token)
    {
        if (token is not { Length: TokenLength })
        {
            return false;
        }

        foreach (char character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character != '-' &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
