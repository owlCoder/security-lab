using System.Security.Cryptography;
using System.Text;
using Platform.Identity.Interfaces;

namespace Platform.Identity.Services;

public sealed class BackupCodeGenerator : IBackupCodeGenerator
{
    private const int CodeByteCount = 16;
    private const int CodeLength = 35;
    private const int HashLength = 64;

    public string GenerateCode()
    {
        byte[] randomBytes =
            RandomNumberGenerator.GetBytes(CodeByteCount);

        string hex = Convert.ToHexString(randomBytes);

        return $"{hex[..8]}-{hex[8..16]}-{hex[16..24]}-{hex[24..]}";
    }

    public bool TryComputeHash(string code, out string codeHash)
    {
        codeHash = string.Empty;

        if (!IsValidCode(code))
        {
            return false;
        }

        string normalizedCode = code.ToUpperInvariant();

        byte[] codeBytes = Encoding.ASCII.GetBytes(normalizedCode);
        byte[] hashBytes = SHA256.HashData(codeBytes);

        codeHash = Convert.ToHexString(hashBytes);

        return true;
    }

    public bool VerifyHash(string code, string storedHash)
    {
        if (!IsValidStoredHash(storedHash))
        {
            return false;
        }

        if (!TryComputeHash(code, out string computedHash))
        {
            return false;
        }

        byte[] computedBytes = Convert.FromHexString(computedHash);
        byte[] storedBytes = Convert.FromHexString(storedHash);

        return CryptographicOperations.FixedTimeEquals(
            computedBytes,
            storedBytes);
    }

    private static bool IsValidCode(string code)
    {
        if (code is not { Length: CodeLength })
        {
            return false;
        }

        for (int index = 0; index < code.Length; index++)
        {
            bool separatorPosition =
                index == 8 || index == 17 || index == 26;

            if (separatorPosition)
            {
                if (code[index] != '-')
                {
                    return false;
                }

                continue;
            }

            if (!char.IsAsciiHexDigit(code[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidStoredHash(string storedHash)
    {
        if (storedHash is not { Length: HashLength })
        {
            return false;
        }

        foreach (char character in storedHash)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
